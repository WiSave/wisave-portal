using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WiSave.Portal.Auth.Models;
using Xunit;

namespace WiSave.Portal.Tests.Auth;

public class AuthEndpointsTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory;
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public AuthEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("UseInMemoryDatabase", "true");
            builder.UseSetting("InMemoryDatabaseName", "AuthTests_" + Guid.NewGuid());
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

        if (!await db.Permissions.AnyAsync())
        {
            var permissions = new[]
            {
                new Permission { Id = Guid.Parse("a3000000-0000-0000-0000-000000000001"), Name = "expenses:read" },
                new Permission { Id = Guid.Parse("a3000000-0000-0000-0000-000000000002"), Name = "expenses:write" },
                new Permission { Id = Guid.Parse("a3000000-0000-0000-0000-000000000003"), Name = "expenses:delete" },
            };
            db.Permissions.AddRange(permissions);
            await db.SaveChangesAsync();

            db.PlanPermissions.AddRange(
                new PlanPermission { PlanId = "free", PermissionId = permissions[0].Id },
                new PlanPermission { PlanId = "standard", PermissionId = permissions[0].Id },
                new PlanPermission { PlanId = "standard", PermissionId = permissions[1].Id },
                new PlanPermission { PlanId = "premium", PermissionId = permissions[0].Id },
                new PlanPermission { PlanId = "premium", PermissionId = permissions[1].Id },
                new PlanPermission { PlanId = "premium", PermissionId = permissions[2].Id }
            );
            await db.SaveChangesAsync();
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Register_ValidData_ReturnsUserAndSetsCookie()
    {
        var client = CreateClient();
        var request = new RegisterRequest("Test User", "test@example.com", "Password123!", "free");

        var response = await RegisterAsync(client, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(CancellationToken);
        Assert.NotNull(auth);
        Assert.Equal("Test User", auth.User.Name);
        Assert.Equal("test@example.com", auth.User.Email);
        Assert.NotEmpty(auth.User.Id);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"),
            c => c.Contains("WiSave.Session"));
    }

    [Fact]
    public async Task Register_DuplicateEmail_Returns400()
    {
        var client = CreateClient();
        var request = new RegisterRequest("User", "dupe@example.com", "Password123!", "free");

        await RegisterAsync(client, request);
        var response = await PostWithAntiforgeryAsync(client, "/api/auth/register", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_ValidCredentials_ReturnsUserAndSetsCookie()
    {
        var client = CreateClient();
        var register = new RegisterRequest("Login User", "login@example.com", "Password123!", "free");
        var login = new LoginRequest("login@example.com", "Password123!");

        await RegisterAsync(client, register);
        var response = await PostWithAntiforgeryAsync(client, "/api/auth/login", login);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(CancellationToken);
        Assert.NotNull(auth);
        Assert.Equal("Login User", auth.User.Name);
        Assert.Equal("login@example.com", auth.User.Email);
    }

    [Fact]
    public async Task Login_InvalidPassword_Returns401()
    {
        var client = CreateClient();

        var register = new RegisterRequest("User", "wrong@example.com", "Password123!", "free");
        await RegisterAsync(client, register);

        var login = new LoginRequest("wrong@example.com", "WrongPassword!");
        var response = await PostWithAntiforgeryAsync(client, "/api/auth/login", login);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_Authenticated_ReturnsUser()
    {
        var client = CreateClient();

        var register = new RegisterRequest("Me User", "me@example.com", "Password123!", "free");
        await RegisterAsync(client, register);
        var response = await client.GetAsync("/api/auth/me", CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var user = await response.Content.ReadFromJsonAsync<UserResponse>(CancellationToken);
        Assert.NotNull(user);
        Assert.Equal("Me User", user.Name);
        Assert.Equal("me@example.com", user.Email);
    }

    [Fact]
    public async Task Me_Authenticated_ReturnsPermissions()
    {
        var client = CreateClient(handleCookies: true);
        await RegisterAsync(client, new RegisterRequest("Perm User", "perm@example.com", "Password123!", "free"));

        var response = await client.GetAsync("/api/auth/me", CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var user = await response.Content.ReadFromJsonAsync<UserResponse>(CancellationToken);
        Assert.NotNull(user);
        Assert.NotNull(user.Permissions);
        Assert.Contains("expenses:read", user.Permissions);
    }

    [Fact]
    public async Task Me_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });

        var response = await client.GetAsync("/api/auth/me", CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Logout_ClearsSession()
    {
        var client = CreateClient();

        var register = new RegisterRequest("Logout User", "logout@example.com", "Password123!", "free");
        await RegisterAsync(client, register);

        var logoutResponse = await PostWithAntiforgeryAsync(client, "/api/auth/logout", new { });
        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);

        var meResponse = await client.GetAsync("/api/auth/me", CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, meResponse.StatusCode);
    }

    [Fact]
    public async Task Register_SetsCookieWithSecureFlag()
    {
        // Tests run over HTTPS (BaseAddress = https://localhost) and with
        // SameAsRequest policy, so the cookie should have the Secure flag.
        var client = CreateClient();
        var request = new RegisterRequest("Secure User", "secure@example.com", "Password123!", "free");

        var response = await RegisterAsync(client, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var setCookie = response.Headers.GetValues("Set-Cookie").First(c => c.Contains("WiSave.Session"));
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_FiveFailedAttempts_LocksOutAccount()
    {
        var client = CreateClient();
        var register = new RegisterRequest("Lockout User", "lockout@example.com", "Password123!", "free");
        await RegisterAsync(client, register);

        var badLogin = new LoginRequest("lockout@example.com", "WrongPassword!");

        for (var i = 0; i < 5; i++)
        {
            var attempt = await PostWithAntiforgeryAsync(client, "/api/auth/login", badLogin);
            Assert.Equal(HttpStatusCode.Unauthorized, attempt.StatusCode);
        }

        // 6th attempt should also return 401 (not 429, to avoid account-state oracle)
        var lockedOut = await PostWithAntiforgeryAsync(client, "/api/auth/login", badLogin);
        Assert.Equal(HttpStatusCode.Unauthorized, lockedOut.StatusCode);

        // Even correct password should return 401 while locked out
        var correctLogin = new LoginRequest("lockout@example.com", "Password123!");
        var stillLocked = await PostWithAntiforgeryAsync(client, "/api/auth/login", correctLogin);
        Assert.Equal(HttpStatusCode.Unauthorized, stillLocked.StatusCode);
    }

    [Fact]
    public async Task Login_ExceedsRateLimit_Returns429()
    {
        var client = CreateClient();

        HttpResponseMessage? lastResponse = null;
        for (var i = 0; i < 11; i++)
        {
            var login = new LoginRequest($"ratelimit{i}@example.com", "Password123!");
            lastResponse = await PostWithAntiforgeryAsync(client, "/api/auth/login", login);
        }

        Assert.Equal((HttpStatusCode)429, lastResponse!.StatusCode);
    }

    [Fact]
    public async Task Login_WithoutAntiforgeryToken_Returns400()
    {
        var client = CreateClient();
        var register = new RegisterRequest("Csrf User", "csrf@example.com", "Password123!", "free");
        await RegisterAsync(client, register);

        // Logout first
        var logoutResponse = await PostWithAntiforgeryAsync(client, "/api/auth/logout", new { });
        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);

        // Try login WITHOUT antiforgery token
        var login = new LoginRequest("csrf@example.com", "Password123!");
        var response = await client.PostAsJsonAsync("/api/auth/login", login, CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AntiforgeryToken_Anonymous_Returns200()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });

        var response = await client.GetAsync("/api/auth/antiforgery-token", CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AntiforgeryToken_SetsReadableXsrfCookie()
    {
        var client = CreateClient();

        var response = await client.GetAsync("/api/auth/antiforgery-token", CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("XSRF-TOKEN="));
    }

    [Fact]
    public async Task Login_ValidCredentials_RefreshesXsrfCookie()
    {
        var client = CreateClient();
        await RegisterAsync(client, new RegisterRequest("Token User", "token@example.com", "Password123!", "free"));

        var logoutResponse = await PostWithAntiforgeryAsync(client, "/api/auth/logout", new { });
        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);

        var response = await PostWithAntiforgeryAsync(
            client,
            "/api/auth/login",
            new LoginRequest("token@example.com", "Password123!"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("XSRF-TOKEN="));
    }

    // Creates an HttpClient backed by a CookieContainer (via CookieDelegatingHandler) so
    // that session cookies are sent on every request for authenticated flows.
    private HttpClient CreateClient(bool handleCookies = true)
    {
        var cookieContainer = new CookieContainer();
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

        // The endpoint sets a non-HttpOnly XSRF-TOKEN cookie with the request token.
        var xsrfCookie = response.Headers.GetValues("Set-Cookie")
            .First(c => c.StartsWith("XSRF-TOKEN="));
        return Uri.UnescapeDataString(xsrfCookie.Split('=', 2)[1].Split(';')[0]);
    }

    private static async Task<HttpResponseMessage> PostWithAntiforgeryAsync<T>(
        HttpClient client, string url, T body)
    {
        var token = await GetAntiforgeryTokenAsync(client);
        var message = new HttpRequestMessage(HttpMethod.Post, url);
        message.Headers.Add("X-XSRF-TOKEN", token);
        message.Content = JsonContent.Create(body);
        return await client.SendAsync(message, CancellationToken);
    }

    private static Task<HttpResponseMessage> RegisterAsync(HttpClient client, RegisterRequest request) =>
        PostWithAntiforgeryAsync(client, "/api/auth/register", request);

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
}
