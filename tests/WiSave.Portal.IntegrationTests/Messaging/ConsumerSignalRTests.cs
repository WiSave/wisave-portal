using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using WiSave.Expenses.Contracts.Events;
using WiSave.Expenses.Contracts.Models;
using WiSave.Portal.Auth.Models;
using WiSave.Portal.Authorization;
using Wolverine;
using Xunit;

namespace WiSave.Portal.IntegrationTests.Messaging;

public class ConsumerSignalRTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory;
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public ConsumerSignalRTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("UseInMemoryDatabase", "true");
            builder.UseSetting("InMemoryDatabaseName", "ConsumerTests_" + Guid.NewGuid());
            builder.UseSetting("Messaging:Transport", "InMemory");
            builder.UseSetting("Redis:ConnectionString", "");
        });
    }

    public async ValueTask InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in PortalRoles.AdminRoles.Concat(PortalRoles.PlanRoles))
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public void Services_ExposeWolverineMessageBus()
    {
        using var scope = _factory.Services.CreateScope();

        var bus = scope.ServiceProvider.GetService<IMessageBus>();

        Assert.NotNull(bus);
    }

    [Fact]
    public async Task ExpenseCreated_IsPushedToSignalRClient()
    {
        var (connection, userIdText) = await CreateAuthenticatedHubConnection("expense@example.com");
        var userId = Guid.Parse(userIdText);
        var expenseId = Guid.NewGuid();

        var tcs = new TaskCompletionSource<JsonElement>();
        connection.On<JsonElement>("realtimeEvent", envelope =>
        {
            if (envelope.GetProperty("eventType").GetString() == "expense.created")
                tcs.TrySetResult(envelope);
        });

        await connection.StartAsync(CancellationToken);

        await PublishExpensesEvent(new ExpenseCreated(
            Id: new ExpenseId(expenseId),
            Amount: new Money(99.99m, Currency.PLN),
            ExpenseDate: new DateOnly(2026, 4, 1),
            Name: "Lunch",
            Description: "Test expense",
            UserId: userId,
            Tags: ["food"]));

        var envelope = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken);
        Assert.Equal("expenses", envelope.GetProperty("domain").GetString());
        Assert.Equal("expense.created", envelope.GetProperty("eventType").GetString());
        Assert.Equal(expenseId.ToString(), envelope.GetProperty("entityId").GetString());
        var payload = envelope.GetProperty("payload");
        Assert.Equal(expenseId.ToString(), payload.GetProperty("id").GetProperty("value").GetString());
        Assert.Equal(userId, payload.GetProperty("userId").GetGuid());
        Assert.Equal("Lunch", payload.GetProperty("name").GetString());

        await connection.StopAsync(CancellationToken);
        await connection.DisposeAsync();
    }

    private async Task<(HubConnection Connection, string UserId)> CreateAuthenticatedHubConnection(string email)
    {
        var cookieContainer = new System.Net.CookieContainer();
        var handler = new CookieDelegatingHandler(cookieContainer, _factory.Server.CreateHandler());
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };

        var afToken = await GetAntiforgeryTokenAsync(client);
        var request = new RegisterRequest("Test User", email, "Password123!", "free");
        var msg = new HttpRequestMessage(HttpMethod.Post, "/api/auth/register");
        msg.Headers.Add("X-XSRF-TOKEN", afToken);
        msg.Content = JsonContent.Create(request);
        var registerResponse = await client.SendAsync(msg, CancellationToken);
        var registerBody = await registerResponse.Content.ReadAsStringAsync(CancellationToken);
        Assert.True(
            registerResponse.StatusCode == HttpStatusCode.OK,
            $"Expected 200 from /api/auth/register but got {(int)registerResponse.StatusCode} {registerResponse.StatusCode}. Body: {registerBody}");

        var auth = await registerResponse.Content.ReadFromJsonAsync<AuthResponse>(CancellationToken);
        var userId = auth!.User.Id;

        var cookies = registerResponse.Headers
            .Where(h => h.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase))
            .SelectMany(h => h.Value)
            .ToList();

        var cookieHeader = string.Join("; ",
            cookies.Select(c => c.Split(';')[0]));

        var connection = new HubConnectionBuilder()
            .WithUrl("http://localhost/hubs/notifications", options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Headers.Add("Cookie", cookieHeader);
            })
            .Build();

        return (connection, userId);
    }

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/auth/antiforgery-token", CancellationToken);
        response.EnsureSuccessStatusCode();

        var xsrfCookie = response.Headers.GetValues("Set-Cookie")
            .First(c => c.StartsWith("XSRF-TOKEN="));
        return Uri.UnescapeDataString(xsrfCookie.Split('=', 2)[1].Split(';')[0]);
    }

    private async Task PublishExpensesEvent<T>(T message)
        where T : class
    {
        using var scope = _factory.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        await bus.PublishAsync(message);
    }

    // Delegating handler that manages a cookie container, forwarding cookies on requests
    // and storing cookies from responses — while leaving Set-Cookie headers visible in the response.
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
                    catch (System.Net.CookieException) { /* ignore malformed cookies */ }
                }
            }

            return response;
        }
    }

}
