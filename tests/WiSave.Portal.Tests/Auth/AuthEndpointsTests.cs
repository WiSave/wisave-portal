using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;

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
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Register_ValidData_ReturnsUserAndSetsCookie()
    {
        var client = _factory.CreateClient();
        var request = new RegisterRequest("Test User", "test@example.com", "Password123!", "free");

        var response = await client.PostAsJsonAsync("/api/auth/register", request, CancellationToken);

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
        var client = _factory.CreateClient();
        var request = new RegisterRequest("User", "dupe@example.com", "Password123!", "free");

        await RegisterAsync(client, request);
        var response = await client.PostAsJsonAsync("/api/auth/register", request, CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_ValidCredentials_ReturnsUserAndSetsCookie()
    {
        var client = _factory.CreateClient();
        var register = new RegisterRequest("Login User", "login@example.com", "Password123!", "free");
        var login = new LoginRequest("login@example.com", "Password123!");

        await RegisterAsync(client, register);
        var response = await client.PostAsJsonAsync("/api/auth/login", login, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(CancellationToken);
        Assert.NotNull(auth);
        Assert.Equal("Login User", auth.User.Name);
        Assert.Equal("login@example.com", auth.User.Email);
    }

    [Fact]
    public async Task Login_InvalidPassword_Returns401()
    {
        var client = _factory.CreateClient();

        var register = new RegisterRequest("User", "wrong@example.com", "Password123!", "free");
        await RegisterAsync(client, register);

        var login = new LoginRequest("wrong@example.com", "WrongPassword!");
        var response = await client.PostAsJsonAsync("/api/auth/login", login, CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_Authenticated_ReturnsUser()
    {
        var client = CreateClient(handleCookies: true);

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
    public async Task Me_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/auth/me", CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Logout_ClearsSession()
    {
        var client = CreateClient(handleCookies: true);

        var register = new RegisterRequest("Logout User", "logout@example.com", "Password123!", "free");
        await RegisterAsync(client, register);

        var logoutResponse = await client.PostAsync("/api/auth/logout", null, CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);

        var meResponse = await client.GetAsync("/api/auth/me", CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, meResponse.StatusCode);
    }

    private HttpClient CreateClient(bool handleCookies = false) =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = handleCookies
        });

    private static Task<HttpResponseMessage> RegisterAsync(HttpClient client, RegisterRequest request) =>
        client.PostAsJsonAsync("/api/auth/register", request, CancellationToken);
}
