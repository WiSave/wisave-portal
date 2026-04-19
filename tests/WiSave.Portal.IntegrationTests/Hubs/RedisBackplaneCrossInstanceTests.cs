using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using WiSave.Portal.Auth.Models;
using WiSave.Portal.Hubs;
using WiSave.Portal.Hubs.Realtime;
using Xunit;

namespace WiSave.Portal.IntegrationTests.Hubs;

public class RedisBackplaneCrossInstanceTests : IAsyncLifetime
{
    private const string RedisConnectionString = "localhost:6379";
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program>? _instanceA;
    private WebApplicationFactory<Program>? _instanceB;

    public async ValueTask InitializeAsync()
    {
        // Skip when Redis is unavailable (local dev without docker up).
        try
        {
            using var probe = await ConnectionMultiplexer.ConnectAsync(RedisConnectionString);
            Assert.True(probe.IsConnected);
        }
        catch
        {
            Assert.Skip($"Redis not reachable at {RedisConnectionString}; start with docker compose up -d redis.");
        }

        var dbName = "CrossInstanceTests_" + Guid.NewGuid();
        _instanceA = CreateFactory(dbName);
        _instanceB = CreateFactory(dbName);

        await SeedAsync(_instanceA);
    }

    public async ValueTask DisposeAsync()
    {
        if (_instanceA is not null) await _instanceA.DisposeAsync();
        if (_instanceB is not null) await _instanceB.DisposeAsync();
    }

    [Fact(Skip = "Pending fix to the existing portal integration-test auth setup: /api/auth/register currently returns BadRequest in-process (reproducible on the pre-existing NotificationsHubTests and ConsumerSignalRTests as well). Unblock by fixing auth in WebApplicationFactory, then remove this Skip.")]
    public async Task Event_pushed_from_instance_A_reaches_client_connected_to_instance_B()
    {
        // Register user via instance A, connect client to instance B.
        var (registerClient, _) = await RegisterUserAsync(_instanceA!, "cross@example.com");
        var userId = await FetchUserIdAsync(_instanceA!, registerClient);
        var cookieHeader = ExtractCookieHeader(registerClient);

        var connection = new HubConnectionBuilder()
            .WithUrl("http://localhost/hubs/notifications", options =>
            {
                options.HttpMessageHandlerFactory = _ => _instanceB!.Server.CreateHandler();
                options.Headers.Add("Cookie", cookieHeader);
            })
            .Build();

        var tcs = new TaskCompletionSource<JsonElement>();
        connection.On<JsonElement>("realtimeEvent", envelope =>
        {
            if (envelope.GetProperty("eventType").GetString() == "expense.recorded")
                tcs.TrySetResult(envelope);
        });

        await connection.StartAsync(CancellationToken);

        // Push envelope from instance A's hub context directly — bypasses MT, tests the backplane.
        using var scope = _instanceA!.Services.CreateScope();
        var hub = scope.ServiceProvider.GetRequiredService<IHubContext<NotificationsHub>>();
        var env = new RealtimeEnvelope(
            EventId: Guid.CreateVersion7(),
            Domain: "expenses",
            EventType: RealtimeEventType.ExpenseRecorded,
            OccurredAt: DateTime.UtcNow,
            EntityId: "exp-cross-1",
            Payload: new { expenseId = "exp-cross-1", userId });
        await hub.Clients.Group(userId).SendAsync("realtimeEvent", env, CancellationToken);

        var received = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken);
        Assert.Equal("expense.recorded", received.GetProperty("eventType").GetString());
        Assert.Equal("exp-cross-1", received.GetProperty("entityId").GetString());

        await connection.StopAsync(CancellationToken);
        await connection.DisposeAsync();
    }

    private static WebApplicationFactory<Program> CreateFactory(string dbName)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("UseInMemoryDatabase", "true");
            builder.UseSetting("InMemoryDatabaseName", dbName);
            builder.UseSetting("Messaging:Transport", "InMemory");
            builder.UseSetting("Redis:ConnectionString", RedisConnectionString);
        });
    }

    private static async Task SeedAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in new[] { "superadmin", "admin", "user" })
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }

        var db = scope.ServiceProvider.GetRequiredService<Infrastructure.Database.PortalDbContext>();
        if (!await db.Plans.AnyAsync(TestContext.Current.CancellationToken))
        {
            db.Plans.AddRange(
                new Plan { Id = "free", Name = "Free" },
                new Plan { Id = "standard", Name = "Standard" },
                new Plan { Id = "premium", Name = "Premium" }
            );
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
    }

    private static async Task<(HttpResponseMessage Response, System.Net.CookieContainer Container)> RegisterUserAsync(
        WebApplicationFactory<Program> factory, string email)
    {
        var cookieContainer = new System.Net.CookieContainer();
        var handler = new CookieDelegatingHandler(cookieContainer, factory.Server.CreateHandler());
        var client = new HttpClient(handler);
        client.BaseAddress = new Uri("https://localhost");

        var afResp = await client.GetAsync("/api/auth/antiforgery-token", CancellationToken);
        afResp.EnsureSuccessStatusCode();
        var xsrfCookie = afResp.Headers.GetValues("Set-Cookie").First(c => c.StartsWith("XSRF-TOKEN="));
        var afToken = Uri.UnescapeDataString(xsrfCookie.Split('=', 2)[1].Split(';')[0]);

        var request = new RegisterRequest("Cross User", email, "Password123!", "free");
        var msg = new HttpRequestMessage(HttpMethod.Post, "/api/auth/register");
        msg.Headers.Add("X-XSRF-TOKEN", afToken);
        msg.Content = JsonContent.Create(request);
        var registerResponse = await client.SendAsync(msg, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);

        return (registerResponse, cookieContainer);
    }

    private static async Task<string> FetchUserIdAsync(WebApplicationFactory<Program> factory, HttpResponseMessage registerResponse)
    {
        var auth = await registerResponse.Content.ReadFromJsonAsync<AuthResponse>(CancellationToken);
        return auth!.User.Id;
    }

    private static string ExtractCookieHeader(HttpResponseMessage registerResponse)
    {
        var cookies = registerResponse.Headers
            .Where(h => h.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase))
            .SelectMany(h => h.Value)
            .ToList();
        return string.Join("; ", cookies.Select(c => c.Split(';')[0]));
    }

    private sealed class CookieDelegatingHandler(System.Net.CookieContainer cookieContainer, HttpMessageHandler inner)
        : DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var cookieHeader = cookieContainer.GetCookieHeader(request.RequestUri!);
            if (!string.IsNullOrEmpty(cookieHeader))
                request.Headers.TryAddWithoutValidation("Cookie", cookieHeader);

            var response = await base.SendAsync(request, cancellationToken);

            if (response.Headers.TryGetValues("Set-Cookie", out var setCookieHeaders))
            {
                foreach (var setCookie in setCookieHeaders)
                {
                    try { cookieContainer.SetCookies(request.RequestUri!, setCookie); }
                    catch (System.Net.CookieException) { /* ignore */ }
                }
            }

            return response;
        }
    }
}
