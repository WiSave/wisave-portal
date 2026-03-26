using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WiSave.Portal.Auth.Models;
using Xunit;

namespace WiSave.Portal.Tests.Gateway;

public class UserHeaderTransformTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private DownstreamEchoServer _downstream = null!;
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _downstream = await DownstreamEchoServer.StartAsync(CancellationToken);
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("UseInMemoryDatabase", "true");
            builder.UseSetting("InMemoryDatabaseName", "GatewayTests_" + Guid.NewGuid());
            builder.UseSetting("Redis:ConnectionString", "");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ReverseProxy:Clusters:incomes-cluster:Destinations:destination1:Address"] = _downstream.BaseAddress
                });
            });
        });
        await SeedRolesAsync();
    }

    public async ValueTask DisposeAsync()
    {
        _factory.Dispose();
        await _downstream.DisposeAsync();
    }

    private async Task SeedRolesAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.RoleManager<Microsoft.AspNetCore.Identity.IdentityRole>>();
        foreach (var role in new[] { "superadmin", "admin", "user" })
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new Microsoft.AspNetCore.Identity.IdentityRole(role));
        }

        var db = scope.ServiceProvider.GetRequiredService<WiSave.Portal.Infrastructure.Database.PortalDbContext>();
        if (!await db.Plans.AnyAsync())
        {
            db.Plans.AddRange(
                new WiSave.Portal.Auth.Models.Plan { Id = "free", Name = "Free" },
                new WiSave.Portal.Auth.Models.Plan { Id = "standard", Name = "Standard" },
                new WiSave.Portal.Auth.Models.Plan { Id = "premium", Name = "Premium" }
            );
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task ProxiedRequest_Authenticated_ForwardsIdentityHeaders()
    {
        var client = CreateClient(handleCookies: true);
        var auth = await RegisterAsync(client, "Proxy User", "proxy@example.com");

        var response = await client.GetAsync("/api/incomes", TestContext.Current.CancellationToken);
        var forwarded = await response.Content.ReadFromJsonAsync<ForwardedRequest>(CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(forwarded);
        Assert.Equal("/incomes", forwarded.Path);
        Assert.Equal(auth.User.Id, GetHeaderValue(forwarded, "X-User-Id"));
        Assert.Equal(auth.User.Email, GetHeaderValue(forwarded, "X-User-Email"));
    }

    [Fact]
    public async Task ProxiedRequest_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/incomes", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProxiedRequest_ClientSpoofedHeaders_AreOverwritten()
    {
        var client = CreateClient(handleCookies: true);
        var auth = await RegisterAsync(client, "Spoof User", "spoof@example.com");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/incomes");
        request.Headers.TryAddWithoutValidation("X-User-Id", "spoofed-id");
        request.Headers.TryAddWithoutValidation("X-User-Email", "evil@attacker.com");
        request.Headers.TryAddWithoutValidation("X-User-Roles", "admin");

        var response = await client.SendAsync(request, CancellationToken);
        var forwarded = await response.Content.ReadFromJsonAsync<ForwardedRequest>(CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(forwarded);
        Assert.Equal(auth.User.Id, GetHeaderValue(forwarded, "X-User-Id"));
        Assert.Equal(auth.User.Email, GetHeaderValue(forwarded, "X-User-Email"));
        // Spoofed "admin" role should be overwritten with actual "user" role
        Assert.Equal("user", GetHeaderValue(forwarded, "X-User-Roles"));
    }

    private HttpClient CreateClient(bool handleCookies = false) =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = handleCookies
        });

    private static async Task<AuthResponse> RegisterAsync(HttpClient client, string name, string email)
    {
        var request = new RegisterRequest(name, email, "Password123!", "free");
        var response = await client.PostAsJsonAsync("/api/auth/register", request, CancellationToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<AuthResponse>(CancellationToken))!;
    }

    private static string GetHeaderValue(ForwardedRequest forwarded, string headerName)
    {
        Assert.True(forwarded.Headers.TryGetValue(headerName, out var values));
        return Assert.Single(values);
    }

    private sealed record ForwardedRequest(string Path, Dictionary<string, string[]> Headers);

    private sealed class DownstreamEchoServer : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private DownstreamEchoServer(WebApplication app, string baseAddress)
        {
            _app = app;
            BaseAddress = baseAddress;
        }

        public string BaseAddress { get; }

        public static async Task<DownstreamEchoServer> StartAsync(CancellationToken cancellationToken)
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");

            var app = builder.Build();
            app.Map("/{**remainder}", (HttpContext context) =>
            {
                var headers = context.Request.Headers.ToDictionary(
                    header => header.Key,
                    header => header.Value.Select(static value => value ?? string.Empty).ToArray(),
                    StringComparer.OrdinalIgnoreCase);

                return Results.Json(new ForwardedRequest(context.Request.Path.Value ?? string.Empty, headers));
            });

            await app.StartAsync(cancellationToken);

            var addresses = app.Services.GetRequiredService<IServer>()
                .Features
                .Get<IServerAddressesFeature>()?
                .Addresses;

            var baseAddress = addresses?.SingleOrDefault()
                ?? throw new InvalidOperationException("Unable to determine downstream server address.");

            return new DownstreamEchoServer(app, baseAddress);
        }

        public ValueTask DisposeAsync() => _app.DisposeAsync();
    }
}
