using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
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
        // Register a user and capture session cookie
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var request = new RegisterRequest("Hub User", "hub@example.com", "Password123!", "free");
        var registerResponse = await client.PostAsJsonAsync("/api/auth/register", request, CancellationToken);
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
}
