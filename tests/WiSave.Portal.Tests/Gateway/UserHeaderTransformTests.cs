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
using WiSave.Portal.Contracts.Authorization;
using WiSave.Portal.Contracts.Identity;
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
        await _factory.DisposeAsync();
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

        if (!await db.Permissions.AnyAsync())
        {
            var incomesRead = new WiSave.Portal.Auth.Models.Permission
            {
                Id = Guid.Parse("a1000000-0000-0000-0000-000000000001"),
                Name = PortalPermissions.Incomes.Read,
                Description = "View incomes"
            };
            db.Permissions.Add(incomesRead);
            await db.SaveChangesAsync();

            db.PlanPermissions.Add(new WiSave.Portal.Auth.Models.PlanPermission
            {
                PlanId = "free",
                PermissionId = incomesRead.Id
            });
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task ProxiedRequest_Authenticated_ForwardsIdentityHeaders()
    {
        var client = CreateClientWithCookies();
        var auth = await RegisterAsync(client, "Proxy User", "proxy@example.com");

        var response = await client.GetAsync("/api/incomes", TestContext.Current.CancellationToken);
        var forwarded = await response.Content.ReadFromJsonAsync<ForwardedRequest>(CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(forwarded);
        Assert.Equal("/incomes", forwarded.Path);
        Assert.Equal(auth.User.Id, GetHeaderValue(forwarded, PortalHeaderNames.UserId));
        Assert.Equal(auth.User.Email, GetHeaderValue(forwarded, PortalHeaderNames.UserEmail));
        Assert.Equal(PortalPermissions.Incomes.Read, GetHeaderValue(forwarded, PortalHeaderNames.UserPermissions));
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
        var client = CreateClientWithCookies();
        var auth = await RegisterAsync(client, "Spoof User", "spoof@example.com");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/incomes");
        request.Headers.TryAddWithoutValidation(PortalHeaderNames.UserId, "spoofed-id");
        request.Headers.TryAddWithoutValidation(PortalHeaderNames.UserEmail, "evil@attacker.com");
        request.Headers.TryAddWithoutValidation(PortalHeaderNames.UserRoles, "admin");

        var response = await client.SendAsync(request, CancellationToken);
        var forwarded = await response.Content.ReadFromJsonAsync<ForwardedRequest>(CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(forwarded);
        Assert.Equal(auth.User.Id, GetHeaderValue(forwarded, PortalHeaderNames.UserId));
        Assert.Equal(auth.User.Email, GetHeaderValue(forwarded, PortalHeaderNames.UserEmail));
        // Spoofed "admin" role should be overwritten with actual "user" role
        Assert.Equal("user", GetHeaderValue(forwarded, PortalHeaderNames.UserRoles));
    }

    [Fact]
    public async Task UnsafeProxyRequest_WithoutAntiforgeryToken_Returns400()
    {
        var client = CreateClientWithCookies();
        await RegisterAsync(client, "Proxy Post User", "proxy-post@example.com");

        var response = await client.PostAsJsonAsync("/api/incomes", new { name = "Test" }, CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UnsafeProxyRequest_WithAntiforgeryToken_ForwardsRequest()
    {
        var client = CreateClientWithCookies();
        await RegisterAsync(client, "Proxy Post User", "proxy-post-ok@example.com");

        var token = await GetAntiforgeryTokenAsync(client);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/incomes")
        {
            Content = JsonContent.Create(new { name = "Test" })
        };
        request.Headers.Add("X-XSRF-TOKEN", token);

        var response = await client.SendAsync(request, CancellationToken);
        var forwarded = await response.Content.ReadFromJsonAsync<ForwardedRequest>(CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(forwarded);
        Assert.Equal("/incomes", forwarded.Path);
    }

    // Creates an HttpClient backed by a CookieContainer (via CookieDelegatingHandler) so
    // that session cookies are sent on every request for authenticated flows.
    private HttpClient CreateClientWithCookies()
    {
        var cookieContainer = new System.Net.CookieContainer();
        var handler = new CookieDelegatingHandler(cookieContainer, _factory.Server.CreateHandler());
        return new HttpClient(handler)
        {
            BaseAddress = new Uri("https://localhost"),
        };
    }

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/auth/antiforgery-token", CancellationToken);
        response.EnsureSuccessStatusCode();

        var xsrfCookie = response.Headers.GetValues("Set-Cookie")
            .First(c => c.StartsWith("XSRF-TOKEN="));
        return Uri.UnescapeDataString(xsrfCookie.Split('=', 2)[1].Split(';')[0]);
    }

    private static async Task<AuthResponse> RegisterAsync(HttpClient client, string name, string email)
    {
        var token = await GetAntiforgeryTokenAsync(client);
        var request = new RegisterRequest(name, email, "Password123!", "free");
        var message = new HttpRequestMessage(HttpMethod.Post, "/api/auth/register");
        message.Headers.Add("X-XSRF-TOKEN", token);
        message.Content = JsonContent.Create(request);
        var response = await client.SendAsync(message, CancellationToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<AuthResponse>(CancellationToken))!;
    }

    private static string GetHeaderValue(ForwardedRequest forwarded, string headerName)
    {
        Assert.True(forwarded.Headers.TryGetValue(headerName, out var values));
        return Assert.Single(values);
    }

    private sealed record ForwardedRequest(string Path, Dictionary<string, string[]> Headers);

    // Delegating handler that manages a cookie container, forwarding cookies on requests
    // and storing cookies from responses — while leaving Set-Cookie headers visible in the response.
    private sealed class CookieDelegatingHandler(CookieContainer cookieContainer, HttpMessageHandler inner)
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
                    catch (CookieException) { /* ignore malformed cookies */ }
                }
            }

            return response;
        }
    }

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
