using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using WiSave.Portal.Auth.Models;
using WiSave.Portal.Authorization;
using WiSave.Portal.Contracts.Authorization;
using Xunit;

namespace WiSave.Portal.IntegrationTests.Auth;

public class AdminAccessManagementEndpointsTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory;
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public AdminAccessManagementEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("UseInMemoryDatabase", "true");
            builder.UseSetting("InMemoryDatabaseName", "AdminAccessTests_" + Guid.NewGuid());
            builder.UseSetting("Redis:ConnectionString", "");
        });
    }

    public async ValueTask InitializeAsync()
    {
        await SeedIdentityDataAsync(_factory);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task AccessManagement_NormalUser_ReturnsForbidden()
    {
        var client = CreateClient();
        await RegisterAsync(client, new RegisterRequest("Normal User", "normal@example.com", "Password123!", "free"));

        var response = await client.GetAsync("/api/admin/access-management", CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AccessManagement_Admin_ReturnsRolesUsersAndCapabilities()
    {
        var client = CreateClient();
        await RegisterAsync(client, new RegisterRequest("Admin User", "admin@example.com", "Password123!", "free"));
        await AddUserToRoleAsync("admin@example.com", PortalRoles.Admin);

        await CreateUserAsync("Target User", "target@example.com", PortalRoles.StandardPlan);

        var response = await client.GetAsync("/api/admin/access-management", CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var access = await response.Content.ReadFromJsonAsync<AccessManagementResponse>(CancellationToken);
        Assert.NotNull(access);
        Assert.False(access.CanManagePrivilegedRoles);
        Assert.Contains(PortalPermissions.Stocks.Read, access.AvailablePermissions);
        Assert.Contains(access.Roles, role => role.Name == PortalRoles.Admin);
        Assert.Contains(access.Roles, role => role.Permissions.Contains(PortalPermissions.Incomes.Read));

        var targetUser = Assert.Single(access.Users, user => user.Email == "target@example.com");
        Assert.True(targetUser.CanEditRoles);
        Assert.Contains(access.Roles.Single(role => role.Name == PortalRoles.StandardPlan).Id, targetUser.Roles);
        Assert.Contains(PortalPermissions.Expenses.Read, targetUser.Permissions);
    }

    [Fact]
    public async Task UpdateUserRoles_Admin_CanUpdateNormalUserPlanRole()
    {
        var client = CreateClient();
        await RegisterAsync(client, new RegisterRequest("Admin User", "plan-admin@example.com", "Password123!", "free"));
        await AddUserToRoleAsync("plan-admin@example.com", PortalRoles.Admin);

        var target = await CreateUserAsync("Target User", "plan-target@example.com", PortalRoles.FreePlan);
        var standardRole = await FindRoleByNameAsync(PortalRoles.StandardPlan);

        var response = await PutWithAntiforgeryAsync(
            client,
            $"/api/admin/access-management/users/{target.Id}/roles",
            new UpdateUserRolesRequest([standardRole.Id]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<AccessUserResponse>(CancellationToken);
        Assert.NotNull(updated);
        Assert.Equal([standardRole.Id], updated.Roles);
        Assert.Contains(PortalPermissions.Expenses.Read, updated.Permissions);
    }

    [Fact]
    public async Task UpdateUserRoles_Admin_CannotAssignPrivilegedRole()
    {
        var client = CreateClient();
        await RegisterAsync(client, new RegisterRequest("Admin User", "limited-admin@example.com", "Password123!", "free"));
        await AddUserToRoleAsync("limited-admin@example.com", PortalRoles.Admin);

        var target = await CreateUserAsync("Target User", "limited-target@example.com", PortalRoles.FreePlan);
        var adminRole = await FindRoleByNameAsync(PortalRoles.Admin);

        var response = await PutWithAntiforgeryAsync(
            client,
            $"/api/admin/access-management/users/{target.Id}/roles",
            new UpdateUserRolesRequest([adminRole.Id]));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateUserRoles_Admin_CannotEditPrivilegedUsers()
    {
        var client = CreateClient();
        await RegisterAsync(client, new RegisterRequest("Admin User", "peer-admin@example.com", "Password123!", "free"));
        await AddUserToRoleAsync("peer-admin@example.com", PortalRoles.Admin);

        var target = await CreateUserAsync("Target Admin", "target-admin@example.com", PortalRoles.FreePlan);
        await AddUserToRoleAsync("target-admin@example.com", PortalRoles.Admin);
        var standardRole = await FindRoleByNameAsync(PortalRoles.StandardPlan);

        var response = await PutWithAntiforgeryAsync(
            client,
            $"/api/admin/access-management/users/{target.Id}/roles",
            new UpdateUserRolesRequest([standardRole.Id]));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateUserRoles_SuperAdmin_CanAssignPrivilegedRole()
    {
        var client = CreateClient();
        await RegisterAsync(client, new RegisterRequest("Super Admin", "super@example.com", "Password123!", "free"));
        await AddUserToRoleAsync("super@example.com", PortalRoles.SuperAdmin);

        var target = await CreateUserAsync("Target User", "promote-target@example.com", PortalRoles.FreePlan);
        var adminRole = await FindRoleByNameAsync(PortalRoles.Admin);

        var response = await PutWithAntiforgeryAsync(
            client,
            $"/api/admin/access-management/users/{target.Id}/roles",
            new UpdateUserRolesRequest([adminRole.Id]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<AccessUserResponse>(CancellationToken);
        Assert.NotNull(updated);
        Assert.Equal([adminRole.Id], updated.Roles);
        Assert.Contains("*", updated.Permissions);
    }

    [Fact]
    public async Task UpdateUserRoles_RequiresExactlyOneRole()
    {
        var client = CreateClient();
        await RegisterAsync(client, new RegisterRequest("Super Admin", "plan-super@example.com", "Password123!", "free"));
        await AddUserToRoleAsync("plan-super@example.com", PortalRoles.SuperAdmin);

        var target = await CreateUserAsync("Target User", "invalid-plan-target@example.com", PortalRoles.FreePlan);
        var freeRole = await FindRoleByNameAsync(PortalRoles.FreePlan);
        var standardRole = await FindRoleByNameAsync(PortalRoles.StandardPlan);

        var response = await PutWithAntiforgeryAsync(
            client,
            $"/api/admin/access-management/users/{target.Id}/roles",
            new UpdateUserRolesRequest([freeRole.Id, standardRole.Id]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateRolePermissions_Admin_CanReplaceRolePermissionClaims()
    {
        var client = CreateClient();
        await RegisterAsync(client, new RegisterRequest("Admin User", "permission-admin@example.com", "Password123!", "free"));
        await AddUserToRoleAsync("permission-admin@example.com", PortalRoles.Admin);
        var freeRole = await FindRoleByNameAsync(PortalRoles.FreePlan);

        var response = await PutWithAntiforgeryAsync(
            client,
            $"/api/admin/access-management/roles/{freeRole.Id}/permissions",
            new UpdateRolePermissionsRequest([PortalPermissions.Expenses.Read, PortalPermissions.Stocks.Read]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<AccessRoleResponse>(CancellationToken);
        Assert.NotNull(updated);
        Assert.Equal([PortalPermissions.Expenses.Read, PortalPermissions.Stocks.Read], updated.Permissions);
    }

    [Fact]
    public async Task UpdateRolePermissions_NormalUser_ReturnsForbidden()
    {
        var client = CreateClient();
        await RegisterAsync(client, new RegisterRequest("Normal User", "permission-normal@example.com", "Password123!", "free"));
        var freeRole = await FindRoleByNameAsync(PortalRoles.FreePlan);

        var response = await PutWithAntiforgeryAsync(
            client,
            $"/api/admin/access-management/roles/{freeRole.Id}/permissions",
            new UpdateRolePermissionsRequest([PortalPermissions.Expenses.Read]));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateRolePermissions_InvalidPermission_Returns400()
    {
        var client = CreateClient();
        await RegisterAsync(client, new RegisterRequest("Admin User", "permission-invalid@example.com", "Password123!", "free"));
        await AddUserToRoleAsync("permission-invalid@example.com", PortalRoles.Admin);
        var freeRole = await FindRoleByNameAsync(PortalRoles.FreePlan);

        var response = await PutWithAntiforgeryAsync(
            client,
            $"/api/admin/access-management/roles/{freeRole.Id}/permissions",
            new UpdateRolePermissionsRequest(["nope:invalid"]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateRole_Admin_CanCreateCustomRole()
    {
        var client = CreateClient();
        await RegisterAsync(client, new RegisterRequest("Admin User", "role-create@example.com", "Password123!", "free"));
        await AddUserToRoleAsync("role-create@example.com", PortalRoles.Admin);

        var response = await PostWithAntiforgeryAsync(
            client,
            "/api/admin/access-management/roles",
            new CreateRoleRequest("auditor", [PortalPermissions.Incomes.Read]));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<AccessRoleResponse>(CancellationToken);
        Assert.NotNull(created);
        Assert.Equal("auditor", created.Name);
        Assert.Equal(["incomes:read"], created.Permissions);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("superadmin")]
    [InlineData("plan:enterprise")]
    public async Task CreateRole_ReservedRoleName_Returns400(string roleName)
    {
        var client = CreateClient();
        await RegisterAsync(client, new RegisterRequest("Admin User", $"role-reserved-{Guid.NewGuid():N}@example.com", "Password123!", "free"));
        await AddUserToRoleAsync(client, PortalRoles.Admin);

        var response = await PostWithAntiforgeryAsync(
            client,
            "/api/admin/access-management/roles",
            new CreateRoleRequest(roleName, []));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task SeedIdentityDataAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in PortalRoles.AdminRoles.Concat(PortalRoles.PlanRoles))
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }

        await EnsurePermissionClaimAsync(roleManager, PortalRoles.FreePlan, PortalPermissions.Incomes.Read);
        await EnsurePermissionClaimAsync(roleManager, PortalRoles.StandardPlan, PortalPermissions.Expenses.Read);
        await EnsurePermissionClaimAsync(roleManager, PortalRoles.StandardPlan, PortalPermissions.Expenses.Write);
        await EnsurePermissionClaimAsync(roleManager, PortalRoles.PremiumPlan, PortalPermissions.Expenses.Read);
        await EnsurePermissionClaimAsync(roleManager, PortalRoles.PremiumPlan, PortalPermissions.Expenses.Write);
        await EnsurePermissionClaimAsync(roleManager, PortalRoles.PremiumPlan, PortalPermissions.Expenses.Delete);
    }

    private static async Task EnsurePermissionClaimAsync(RoleManager<IdentityRole> roleManager, string roleName, string permission)
    {
        var role = await roleManager.FindByNameAsync(roleName);
        Assert.NotNull(role);

        var claims = await roleManager.GetClaimsAsync(role);
        if (!claims.Any(c => c.Type == PortalClaimTypes.Permission && c.Value == permission))
            await roleManager.AddClaimAsync(role, new Claim(PortalClaimTypes.Permission, permission));
    }

    private HttpClient CreateClient()
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

        var xsrfCookie = response.Headers.GetValues("Set-Cookie")
            .First(c => c.StartsWith("XSRF-TOKEN="));
        return Uri.UnescapeDataString(xsrfCookie.Split('=', 2)[1].Split(';')[0]);
    }

    private static async Task<HttpResponseMessage> PutWithAntiforgeryAsync<T>(HttpClient client, string url, T body)
    {
        var token = await GetAntiforgeryTokenAsync(client);
        var message = new HttpRequestMessage(HttpMethod.Put, url);
        message.Headers.Add("X-XSRF-TOKEN", token);
        message.Content = JsonContent.Create(body);
        return await client.SendAsync(message, CancellationToken);
    }

    private static Task<HttpResponseMessage> RegisterAsync(HttpClient client, RegisterRequest request) =>
        PostWithAntiforgeryAsync(client, "/api/auth/register", request);

    private static async Task<HttpResponseMessage> PostWithAntiforgeryAsync<T>(HttpClient client, string url, T body)
    {
        var token = await GetAntiforgeryTokenAsync(client);
        var message = new HttpRequestMessage(HttpMethod.Post, url);
        message.Headers.Add("X-XSRF-TOKEN", token);
        message.Content = JsonContent.Create(body);
        return await client.SendAsync(message, CancellationToken);
    }

    private async Task<ApplicationUser> CreateUserAsync(string name, string email, string planRole)
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            Name = name,
            Email = email,
            UserName = email
        };

        var createResult = await userManager.CreateAsync(user, "Password123!");
        Assert.True(createResult.Succeeded);
        var roleResult = await userManager.AddToRoleAsync(user, planRole);
        Assert.True(roleResult.Succeeded);
        return user;
    }

    private async Task AddUserToRoleAsync(string email, string roleName)
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        Assert.NotNull(user);

        var result = await userManager.AddToRoleAsync(user, roleName);
        Assert.True(result.Succeeded);
    }

    private async Task AddUserToRoleAsync(HttpClient client, string roleName)
    {
        var me = await client.GetFromJsonAsync<UserResponse>("/api/auth/me", CancellationToken);
        Assert.NotNull(me);

        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByIdAsync(me.Id);
        Assert.NotNull(user);

        var result = await userManager.AddToRoleAsync(user, roleName);
        Assert.True(result.Succeeded);
    }

    private async Task<IdentityRole> FindRoleByNameAsync(string roleName)
    {
        using var scope = _factory.Services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var role = await roleManager.FindByNameAsync(roleName);
        Assert.NotNull(role);
        return role;
    }

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

    private sealed record AccessManagementResponse(
        bool CanManagePrivilegedRoles,
        string[] AvailablePermissions,
        AccessRoleResponse[] Roles,
        AccessUserResponse[] Users);

    private sealed record AccessRoleResponse(
        string Id,
        string Name,
        string NormalizedName,
        string? ConcurrencyStamp,
        string[] Permissions);

    private sealed record AccessUserResponse(
        string Id,
        string Name,
        string Email,
        string[] Roles,
        string[] Permissions,
        bool CanEditRoles);

    private sealed record UpdateUserRolesRequest(string[] RoleIds);

    private sealed record UpdateRolePermissionsRequest(string[] Permissions);

    private sealed record CreateRoleRequest(string Name, string[] Permissions);

    private sealed record UserResponse(string Id, string Name, string Email, string[] Permissions);
}
