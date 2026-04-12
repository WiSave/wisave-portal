using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WiSave.Portal.Auth.Models;
using Xunit;

namespace WiSave.Portal.Tests.Hubs;

public class NotificationsHubTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory;
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public NotificationsHubTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("UseInMemoryDatabase", "true");
            builder.UseSetting("InMemoryDatabaseName", "HubTests_" + Guid.NewGuid());
            builder.UseSetting("Redis:ConnectionString", "");
        });
    }

    public async ValueTask InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in new[] { "superadmin", "admin", "user" })
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }

        var db = scope.ServiceProvider.GetRequiredService<Infrastructure.Database.PortalDbContext>();
        if (!await db.Plans.AnyAsync())
        {
            db.Plans.AddRange(
                new Plan { Id = "free", Name = "Free" },
                new Plan { Id = "standard", Name = "Standard" },
                new Plan { Id = "premium", Name = "Premium" }
            );
            await db.SaveChangesAsync();
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task AuthenticatedClient_CanConnectToHub()
    {
        // Register a user using CSRF-protected endpoint, capturing session cookie
        var cookieContainer = new System.Net.CookieContainer();
        var handler = new CookieDelegatingHandler(cookieContainer, _factory.Server.CreateHandler());
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };

        var afToken = await GetAntiforgeryTokenAsync(client);
        var request = new RegisterRequest("Hub User", "hub@example.com", "Password123!", "free");
        var msg = new HttpRequestMessage(HttpMethod.Post, "/api/auth/register");
        msg.Headers.Add("X-XSRF-TOKEN", afToken);
        msg.Content = JsonContent.Create(request);
        var registerResponse = await client.SendAsync(msg, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);

        var cookies = registerResponse.Headers
            .Where(h => h.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase))
            .SelectMany(h => h.Value)
            .ToList();

        Assert.NotEmpty(cookies);

        var cookieHeader = string.Join("; ",
            cookies.Select(c => c.Split(';')[0]));

        // Build SignalR connection using the test server's handler
        var connection = new HubConnectionBuilder()
            .WithUrl("http://localhost/hubs/notifications", options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Headers.Add("Cookie", cookieHeader);
            })
            .Build();

        await connection.StartAsync(CancellationToken);

        Assert.Equal(HubConnectionState.Connected, connection.State);

        await connection.StopAsync(CancellationToken);
        await connection.DisposeAsync();
    }

    [Fact]
    public async Task UnauthenticatedClient_GetsRejected()
    {
        var connection = new HubConnectionBuilder()
            .WithUrl("http://localhost/hubs/notifications", options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
            })
            .Build();

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => connection.StartAsync(CancellationToken));

        Assert.Contains("401", ex.Message);

        await connection.DisposeAsync();
    }

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/auth/antiforgery-token", CancellationToken);
        response.EnsureSuccessStatusCode();

        var xsrfCookie = response.Headers.GetValues("Set-Cookie")
            .First(c => c.StartsWith("XSRF-TOKEN="));
        return Uri.UnescapeDataString(xsrfCookie.Split('=', 2)[1].Split(';')[0]);
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
