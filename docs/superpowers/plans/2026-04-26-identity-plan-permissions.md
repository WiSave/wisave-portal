# Identity Plan Permissions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace custom plan-permission runtime resolution with ASP.NET Core Identity plan roles and permission role claims while preserving the gateway `X-User-Permissions` contract.

**Architecture:** Plans become Identity roles named `plan:free`, `plan:standard`, and `plan:premium`; permissions become Identity role claims with claim type `permission`. Registration assigns exactly one plan role, login preserves it, and a focused resolver loads permissions from role claims for `PermissionResolutionMiddleware`. Existing custom plan tables remain in the database for now but are no longer used by runtime auth.

**Tech Stack:** ASP.NET Core Identity, EF Core, PostgreSQL/DbUp migrations, YARP transforms, xUnit integration tests, `WebApplicationFactory`.

---

## File Structure

- Create `src/WiSave.Portal/Authorization/PortalClaimTypes.cs`: constants for Identity claim types used by authorization.
- Create `src/WiSave.Portal/Authorization/PortalRoles.cs`: constants and helpers for plan/admin role names.
- Create `src/WiSave.Portal/Authorization/RolePermissionResolver.cs`: resolves effective permissions from Identity roles and role claims.
- Modify `src/WiSave.Portal/Authorization/PermissionResolutionMiddleware.cs`: delegate permission calculation to `RolePermissionResolver`.
- Modify `src/WiSave.Portal/Program.cs`: register `RolePermissionResolver`, remove unused custom permission caches from DI after code no longer uses them.
- Modify `src/WiSave.Portal/Endpoints/AuthEndpoints.cs`: validate selected plan role and assign plan role during registration.
- Modify `src/WiSave.Portal/Auth/Models/ApplicationUser.cs`: remove runtime dependency on `PlanId` after EF model is updated.
- Modify `src/WiSave.Portal/Infrastructure/Database/PortalDbContext.cs`: remove custom plan DbSets/model configuration and `ApplicationUser.PlanId` mapping from runtime model.
- Modify `src/WiSave.Portal.Migrations/Scripts/003_SeedPlansAndPermissions.sql`: seed Identity plan roles and permission role claims.
- Modify `tests/WiSave.Portal.Tests/Auth/AuthEndpointsTests.cs`: seed plan roles, assert plan assignment, invalid plan behavior, and login preservation.
- Modify `tests/WiSave.Portal.Tests/Gateway/UserHeaderTransformTests.cs`: seed permission role claims and assert forwarded permission headers.

## Constants

Use these constants consistently:

```csharp
namespace WiSave.Portal.Authorization;

public static class PortalClaimTypes
{
    public const string Permission = "permission";
}
```

```csharp
namespace WiSave.Portal.Authorization;

public static class PortalRoles
{
    public const string FreePlan = "plan:free";
    public const string StandardPlan = "plan:standard";
    public const string PremiumPlan = "plan:premium";
    public const string Admin = "admin";
    public const string SuperAdmin = "superadmin";

    public static readonly string[] PlanRoles = [FreePlan, StandardPlan, PremiumPlan];
    public static readonly string[] AdminRoles = [Admin, SuperAdmin];

    public static string NormalizePlanInput(string? plan)
    {
        if (string.IsNullOrWhiteSpace(plan))
            return FreePlan;

        var trimmed = plan.Trim();
        return trimmed.StartsWith("plan:", StringComparison.OrdinalIgnoreCase)
            ? trimmed.ToLowerInvariant()
            : $"plan:{trimmed.ToLowerInvariant()}";
    }

    public static bool IsPlanRole(string role) =>
        PlanRoles.Contains(role, StringComparer.OrdinalIgnoreCase);
}
```

## Task 1: Add Role/Claim Constants

**Files:**
- Create: `src/WiSave.Portal/Authorization/PortalClaimTypes.cs`
- Create: `src/WiSave.Portal/Authorization/PortalRoles.cs`

- [ ] **Step 1: Add failing compile references in tests**

Modify `tests/WiSave.Portal.Tests/Auth/AuthEndpointsTests.cs` by adding:

```csharp
using WiSave.Portal.Authorization;
```

Then change the role seeding loop from:

```csharp
foreach (var role in new[] { "superadmin", "admin", "user" })
```

to:

```csharp
foreach (var role in PortalRoles.AdminRoles.Concat(PortalRoles.PlanRoles))
```

- [ ] **Step 2: Run compile to verify missing constants**

Run:

```bash
dotnet test --filter "FullyQualifiedName~WiSave.Portal.Tests.Auth.AuthEndpointsTests"
```

Expected: build fails because `PortalRoles` does not exist.

- [ ] **Step 3: Create constants**

Create `src/WiSave.Portal/Authorization/PortalClaimTypes.cs`:

```csharp
namespace WiSave.Portal.Authorization;

public static class PortalClaimTypes
{
    public const string Permission = "permission";
}
```

Create `src/WiSave.Portal/Authorization/PortalRoles.cs`:

```csharp
namespace WiSave.Portal.Authorization;

public static class PortalRoles
{
    public const string FreePlan = "plan:free";
    public const string StandardPlan = "plan:standard";
    public const string PremiumPlan = "plan:premium";
    public const string Admin = "admin";
    public const string SuperAdmin = "superadmin";

    public static readonly string[] PlanRoles = [FreePlan, StandardPlan, PremiumPlan];
    public static readonly string[] AdminRoles = [Admin, SuperAdmin];

    public static string NormalizePlanInput(string? plan)
    {
        if (string.IsNullOrWhiteSpace(plan))
            return FreePlan;

        var trimmed = plan.Trim();
        return trimmed.StartsWith("plan:", StringComparison.OrdinalIgnoreCase)
            ? trimmed.ToLowerInvariant()
            : $"plan:{trimmed.ToLowerInvariant()}";
    }

    public static bool IsPlanRole(string role) =>
        PlanRoles.Contains(role, StringComparer.OrdinalIgnoreCase);
}
```

- [ ] **Step 4: Run targeted compile/tests**

Run:

```bash
dotnet test --filter "FullyQualifiedName~WiSave.Portal.Tests.Auth.AuthEndpointsTests"
```

Expected: tests compile. Some tests may still fail until later tasks update registration.

- [ ] **Step 5: Commit**

```bash
git add src/WiSave.Portal/Authorization/PortalClaimTypes.cs src/WiSave.Portal/Authorization/PortalRoles.cs tests/WiSave.Portal.Tests/Auth/AuthEndpointsTests.cs
git commit -m "feat: add portal role constants"
```

## Task 2: Assign Plan Roles During Registration

**Files:**
- Modify: `src/WiSave.Portal/Endpoints/AuthEndpoints.cs`
- Modify: `tests/WiSave.Portal.Tests/Auth/AuthEndpointsTests.cs`

- [ ] **Step 1: Add registration plan tests**

Add these helpers to `AuthEndpointsTests`:

```csharp
private async Task<ApplicationUser> FindUserByEmailAsync(string email)
{
    using var scope = _factory.Services.CreateScope();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var user = await userManager.FindByEmailAsync(email);
    return Assert.NotNull(user);
}

private async Task<IList<string>> GetUserRolesAsync(ApplicationUser user)
{
    using var scope = _factory.Services.CreateScope();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    return await userManager.GetRolesAsync(user);
}
```

Add these tests:

```csharp
[Theory]
[InlineData("free", PortalRoles.FreePlan)]
[InlineData("standard", PortalRoles.StandardPlan)]
[InlineData("premium", PortalRoles.PremiumPlan)]
[InlineData("plan:standard", PortalRoles.StandardPlan)]
public async Task Register_ValidPlan_AssignsExactlyOnePlanRole(string requestedPlan, string expectedRole)
{
    var client = _factory.CreateClient();
    var email = $"plan-{Guid.NewGuid():N}@example.com";
    var request = new RegisterRequest("Plan User", email, "Password123!", requestedPlan);

    var response = await client.PostAsJsonAsync("/api/auth/register", request, CancellationToken);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);

    var user = await FindUserByEmailAsync(email);
    var roles = await GetUserRolesAsync(user);
    Assert.Contains(expectedRole, roles);
    Assert.Single(roles.Where(PortalRoles.IsPlanRole));
}

[Fact]
public async Task Register_BlankPlan_DefaultsToFreePlanRole()
{
    var client = _factory.CreateClient();
    var email = $"blank-plan-{Guid.NewGuid():N}@example.com";
    var request = new RegisterRequest("Blank Plan User", email, "Password123!", "");

    var response = await client.PostAsJsonAsync("/api/auth/register", request, CancellationToken);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);

    var user = await FindUserByEmailAsync(email);
    var roles = await GetUserRolesAsync(user);
    Assert.Contains(PortalRoles.FreePlan, roles);
    Assert.Single(roles.Where(PortalRoles.IsPlanRole));
}

[Fact]
public async Task Register_InvalidPlan_Returns400()
{
    var client = _factory.CreateClient();
    var request = new RegisterRequest("Bad Plan User", $"bad-plan-{Guid.NewGuid():N}@example.com", "Password123!", "enterprise");

    var response = await client.PostAsJsonAsync("/api/auth/register", request, CancellationToken);

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
}
```

- [ ] **Step 2: Run tests to verify failure**

Run:

```bash
dotnet test --filter "FullyQualifiedName~WiSave.Portal.Tests.Auth.AuthEndpointsTests"
```

Expected: new plan role tests fail because registration still validates custom `Plans` and assigns `user`.

- [ ] **Step 3: Update registration endpoint**

In `src/WiSave.Portal/Endpoints/AuthEndpoints.cs`, add:

```csharp
using WiSave.Portal.Authorization;
```

Change the `Register` signature from:

```csharp
PortalDbContext db)
```

to:

```csharp
RoleManager<IdentityRole> roleManager)
```

Replace the custom plan validation and user creation block with:

```csharp
var planRole = PortalRoles.NormalizePlanInput(request.PlanId);
if (!PortalRoles.IsPlanRole(planRole) || !await roleManager.RoleExistsAsync(planRole))
{
    return Results.BadRequest(new { errors = new[] { $"Plan '{request.PlanId}' does not exist." } });
}

var user = new ApplicationUser
{
    Name = request.Name,
    Email = request.Email,
    UserName = request.Email
};
```

Replace:

```csharp
await userManager.AddToRoleAsync(user, "user");
```

with:

```csharp
var roleResult = await userManager.AddToRoleAsync(user, planRole);
if (!roleResult.Succeeded)
{
    return Results.BadRequest(new { errors = roleResult.Errors.Select(e => e.Description) });
}
```

Remove these unused usings if present:

```csharp
using Microsoft.EntityFrameworkCore;
using WiSave.Portal.Infrastructure.Database;
```

- [ ] **Step 4: Run targeted auth tests**

Run:

```bash
dotnet test --filter "FullyQualifiedName~WiSave.Portal.Tests.Auth.AuthEndpointsTests"
```

Expected: auth endpoint tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/WiSave.Portal/Endpoints/AuthEndpoints.cs tests/WiSave.Portal.Tests/Auth/AuthEndpointsTests.cs
git commit -m "feat: assign identity plan roles on registration"
```

## Task 3: Resolve Permissions From Identity Role Claims

**Files:**
- Create: `src/WiSave.Portal/Authorization/RolePermissionResolver.cs`
- Modify: `src/WiSave.Portal/Authorization/PermissionResolutionMiddleware.cs`
- Modify: `src/WiSave.Portal/Program.cs`
- Modify: `tests/WiSave.Portal.Tests/Gateway/UserHeaderTransformTests.cs`

- [ ] **Step 1: Seed test role claims**

In `UserHeaderTransformTests`, add:

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using WiSave.Portal.Authorization;
```

Replace `SeedRolesAsync` with:

```csharp
private async Task SeedRolesAsync()
{
    using var scope = _factory.Services.CreateScope();
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    foreach (var role in PortalRoles.AdminRoles.Concat(PortalRoles.PlanRoles))
    {
        if (!await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new IdentityRole(role));
    }

    await EnsurePermissionClaimAsync(roleManager, PortalRoles.FreePlan, "incomes:read");
    await EnsurePermissionClaimAsync(roleManager, PortalRoles.StandardPlan, "incomes:read");
    await EnsurePermissionClaimAsync(roleManager, PortalRoles.StandardPlan, "incomes:write");
    await EnsurePermissionClaimAsync(roleManager, PortalRoles.PremiumPlan, "incomes:read");
    await EnsurePermissionClaimAsync(roleManager, PortalRoles.PremiumPlan, "incomes:write");
    await EnsurePermissionClaimAsync(roleManager, PortalRoles.PremiumPlan, "incomes:delete");
}

private static async Task EnsurePermissionClaimAsync(RoleManager<IdentityRole> roleManager, string roleName, string permission)
{
    var role = await roleManager.FindByNameAsync(roleName);
    Assert.NotNull(role);

    var claims = await roleManager.GetClaimsAsync(role);
    if (!claims.Any(c => c.Type == PortalClaimTypes.Permission && c.Value == permission))
        await roleManager.AddClaimAsync(role, new Claim(PortalClaimTypes.Permission, permission));
}
```

Add this test:

```csharp
[Fact]
public async Task ProxiedRequest_Authenticated_ForwardsPlanPermissions()
{
    var client = CreateClient(handleCookies: true);
    await RegisterAsync(client, "Permission User", "permissions@example.com", "standard");

    var response = await client.GetAsync("/api/incomes", CancellationToken);
    var forwarded = await response.Content.ReadFromJsonAsync<ForwardedRequest>(CancellationToken);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.NotNull(forwarded);
    var permissions = GetHeaderValue(forwarded, "X-User-Permissions").Split(',');
    Assert.Contains("incomes:read", permissions);
    Assert.Contains("incomes:write", permissions);
}
```

Change `RegisterAsync` signature and body in this test file:

```csharp
private static async Task<AuthResponse> RegisterAsync(HttpClient client, string name, string email, string plan = "free")
{
    var request = new RegisterRequest(name, email, "Password123!", plan);
    var response = await client.PostAsJsonAsync("/api/auth/register", request, CancellationToken);
    response.EnsureSuccessStatusCode();

    return (await response.Content.ReadFromJsonAsync<AuthResponse>(CancellationToken))!;
}
```

- [ ] **Step 2: Run gateway test to verify failure**

Run:

```bash
dotnet test --filter "FullyQualifiedName~WiSave.Portal.Tests.Gateway.UserHeaderTransformTests.ProxiedRequest_Authenticated_ForwardsPlanPermissions"
```

Expected: fails because middleware still reads custom plan caches.

- [ ] **Step 3: Add resolver**

Create `src/WiSave.Portal/Authorization/RolePermissionResolver.cs`:

```csharp
using Microsoft.AspNetCore.Identity;
using WiSave.Portal.Auth.Models;

namespace WiSave.Portal.Authorization;

public class RolePermissionResolver(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager)
{
    public async Task<IReadOnlySet<string>> GetPermissionsAsync(ApplicationUser user)
    {
        var roles = await userManager.GetRolesAsync(user);
        if (roles.Any(role => PortalRoles.AdminRoles.Contains(role, StringComparer.OrdinalIgnoreCase)))
            return new HashSet<string> { "*" };

        var permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var roleName in roles)
        {
            var role = await roleManager.FindByNameAsync(roleName);
            if (role is null)
                continue;

            var claims = await roleManager.GetClaimsAsync(role);
            foreach (var claim in claims.Where(c => c.Type == PortalClaimTypes.Permission && !string.IsNullOrWhiteSpace(c.Value)))
            {
                permissions.Add(claim.Value);
            }
        }

        return permissions;
    }
}
```

- [ ] **Step 4: Update middleware and DI**

Replace `PermissionResolutionMiddleware.cs` with:

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using WiSave.Portal.Auth.Models;

namespace WiSave.Portal.Authorization;

public class PermissionResolutionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        UserManager<ApplicationUser> userManager,
        RolePermissionResolver rolePermissionResolver)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await next(context);
            return;
        }

        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
        {
            await next(context);
            return;
        }

        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            await next(context);
            return;
        }

        context.Items["UserPermissions"] = await rolePermissionResolver.GetPermissionsAsync(user);

        await next(context);
    }
}
```

In `Program.cs`, replace:

```csharp
builder.Services.AddSingleton<UserPlanCache>();
builder.Services.AddSingleton<PlanPermissionCache>();
```

with:

```csharp
builder.Services.AddScoped<RolePermissionResolver>();
```

- [ ] **Step 5: Run targeted gateway tests**

Run:

```bash
dotnet test --filter "FullyQualifiedName~WiSave.Portal.Tests.Gateway.UserHeaderTransformTests"
```

Expected: gateway tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/WiSave.Portal/Authorization/RolePermissionResolver.cs src/WiSave.Portal/Authorization/PermissionResolutionMiddleware.cs src/WiSave.Portal/Program.cs tests/WiSave.Portal.Tests/Gateway/UserHeaderTransformTests.cs
git commit -m "feat: resolve permissions from identity role claims"
```

## Task 4: Remove Runtime Custom Plan Model Usage

**Files:**
- Modify: `src/WiSave.Portal/Auth/Models/ApplicationUser.cs`
- Modify: `src/WiSave.Portal/Infrastructure/Database/PortalDbContext.cs`
- Delete: `src/WiSave.Portal/Auth/Models/Plan.cs`
- Delete: `src/WiSave.Portal/Auth/Models/Permission.cs`
- Delete: `src/WiSave.Portal/Auth/Models/PlanPermission.cs`
- Delete: `src/WiSave.Portal/Authorization/UserPlanCache.cs`
- Delete: `src/WiSave.Portal/Authorization/PlanPermissionCache.cs`
- Modify tests if any compile references remain.

- [ ] **Step 1: Search current references**

Run:

```bash
rg "PlanId|DbSet<Plan>|DbSet<Permission>|DbSet<PlanPermission>|UserPlanCache|PlanPermissionCache|new Plan|PlanPermissions|Permissions" src tests
```

Expected: only the old model/config/cache files and test seeding references remain.

- [ ] **Step 2: Remove `PlanId` from `ApplicationUser`**

Update `src/WiSave.Portal/Auth/Models/ApplicationUser.cs`:

```csharp
using Microsoft.AspNetCore.Identity;

namespace WiSave.Portal.Auth.Models;

public class ApplicationUser : IdentityUser
{
    public required string Name { get; set; }
}
```

- [ ] **Step 3: Remove custom DbSets/model configuration**

Update `src/WiSave.Portal/Infrastructure/Database/PortalDbContext.cs`:

```csharp
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using WiSave.Portal.Auth.Models;

namespace WiSave.Portal.Infrastructure.Database;

public class PortalDbContext(DbContextOptions<PortalDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
    }
}
```

- [ ] **Step 4: Delete unused custom model/cache files**

Delete:

```text
src/WiSave.Portal/Auth/Models/Plan.cs
src/WiSave.Portal/Auth/Models/Permission.cs
src/WiSave.Portal/Auth/Models/PlanPermission.cs
src/WiSave.Portal/Authorization/UserPlanCache.cs
src/WiSave.Portal/Authorization/PlanPermissionCache.cs
```

- [ ] **Step 5: Remove old test plan seeding**

In `AuthEndpointsTests.InitializeAsync`, remove the block that resolves `PortalDbContext` and inserts `Plan` entities. The method should only seed roles:

```csharp
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
```

- [ ] **Step 6: Run build/test**

Run:

```bash
dotnet test --filter "FullyQualifiedName~WiSave.Portal.Tests.Auth.AuthEndpointsTests|FullyQualifiedName~WiSave.Portal.Tests.Gateway.UserHeaderTransformTests"
```

Expected: targeted auth and gateway tests pass.

- [ ] **Step 7: Commit**

```bash
git add src/WiSave.Portal/Auth/Models/ApplicationUser.cs src/WiSave.Portal/Infrastructure/Database/PortalDbContext.cs tests/WiSave.Portal.Tests/Auth/AuthEndpointsTests.cs
git rm src/WiSave.Portal/Auth/Models/Plan.cs src/WiSave.Portal/Auth/Models/Permission.cs src/WiSave.Portal/Auth/Models/PlanPermission.cs src/WiSave.Portal/Authorization/UserPlanCache.cs src/WiSave.Portal/Authorization/PlanPermissionCache.cs
git commit -m "refactor: remove runtime custom plan model"
```

## Task 5: Seed Identity Plan Roles and Permission Claims

**Files:**
- Modify: `src/WiSave.Portal.Migrations/Scripts/003_SeedPlansAndPermissions.sql`

- [ ] **Step 1: Replace custom seed script with Identity seeds**

Replace `src/WiSave.Portal.Migrations/Scripts/003_SeedPlansAndPermissions.sql` with:

```sql
-- Seed administrative roles and plan roles.
INSERT INTO "AspNetRoles" ("Id", "Name", "NormalizedName", "ConcurrencyStamp") VALUES
    ('role-superadmin', 'superadmin', 'SUPERADMIN', gen_random_uuid()::text),
    ('role-admin', 'admin', 'ADMIN', gen_random_uuid()::text),
    ('role-plan-free', 'plan:free', 'PLAN:FREE', gen_random_uuid()::text),
    ('role-plan-standard', 'plan:standard', 'PLAN:STANDARD', gen_random_uuid()::text),
    ('role-plan-premium', 'plan:premium', 'PLAN:PREMIUM', gen_random_uuid()::text)
ON CONFLICT ("Id") DO NOTHING;

-- Add permission claims to plan roles. Duplicate claims are avoided by the NOT EXISTS predicate.
WITH role_permissions("RoleId", "ClaimValue") AS (
    VALUES
        ('role-plan-free', 'incomes:read'),

        ('role-plan-standard', 'incomes:read'),
        ('role-plan-standard', 'incomes:write'),
        ('role-plan-standard', 'stocks:read'),
        ('role-plan-standard', 'expenses:read'),
        ('role-plan-standard', 'expenses:write'),

        ('role-plan-premium', 'incomes:read'),
        ('role-plan-premium', 'incomes:write'),
        ('role-plan-premium', 'incomes:delete'),
        ('role-plan-premium', 'incomes:import'),
        ('role-plan-premium', 'stocks:read'),
        ('role-plan-premium', 'stocks:write'),
        ('role-plan-premium', 'stocks:portfolio:manage'),
        ('role-plan-premium', 'stocks:watchlist:manage'),
        ('role-plan-premium', 'expenses:read'),
        ('role-plan-premium', 'expenses:write'),
        ('role-plan-premium', 'expenses:delete')
)
INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue")
SELECT rp."RoleId", 'permission', rp."ClaimValue"
FROM role_permissions rp
WHERE NOT EXISTS (
    SELECT 1
    FROM "AspNetRoleClaims" arc
    WHERE arc."RoleId" = rp."RoleId"
      AND arc."ClaimType" = 'permission'
      AND arc."ClaimValue" = rp."ClaimValue"
);
```

- [ ] **Step 2: Verify migration SQL is syntactically consistent**

Run:

```bash
dotnet build
```

Expected: build passes. SQL script is not compiled, so also manually check table/column names match `001_InitialIdentity.sql`.

- [ ] **Step 3: Commit**

```bash
git add src/WiSave.Portal.Migrations/Scripts/003_SeedPlansAndPermissions.sql
git commit -m "chore: seed identity plan permission claims"
```

## Task 6: Add Admin Permission Coverage and Full Verification

**Files:**
- Modify: `tests/WiSave.Portal.Tests/Gateway/UserHeaderTransformTests.cs`

- [ ] **Step 1: Add helper for admin user**

Add to `UserHeaderTransformTests`:

```csharp
private async Task AddUserToRoleAsync(string email, string role)
{
    using var scope = _factory.Services.CreateScope();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var user = await userManager.FindByEmailAsync(email);
    Assert.NotNull(user);

    var result = await userManager.AddToRoleAsync(user, role);
    Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(e => e.Description)));
}
```

Make sure the file has:

```csharp
using Microsoft.AspNetCore.Identity;
using WiSave.Portal.Auth.Models;
```

- [ ] **Step 2: Add admin wildcard test**

Add:

```csharp
[Fact]
public async Task ProxiedRequest_AdminUser_ForwardsWildcardPermissions()
{
    var client = CreateClient(handleCookies: true);
    await RegisterAsync(client, "Admin User", "admin-user@example.com", "free");
    await AddUserToRoleAsync("admin-user@example.com", PortalRoles.Admin);

    var response = await client.GetAsync("/api/incomes", CancellationToken);
    var forwarded = await response.Content.ReadFromJsonAsync<ForwardedRequest>(CancellationToken);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.NotNull(forwarded);
    Assert.Equal("*", GetHeaderValue(forwarded, "X-User-Permissions"));
}
```

- [ ] **Step 3: Run all tests**

Run:

```bash
dotnet test
```

Expected: all tests pass.

- [ ] **Step 4: Final search for old runtime references**

Run:

```bash
rg "PlanId|UserPlanCache|PlanPermissionCache|PlanPermissions|DbSet<Plan>|DbSet<Permission>|DbSet<PlanPermission>" src tests
```

Expected: no runtime references remain. Migration scripts may still mention old tables only in historical scripts.

- [ ] **Step 5: Commit**

```bash
git add tests/WiSave.Portal.Tests/Gateway/UserHeaderTransformTests.cs
git commit -m "test: cover identity permission forwarding"
```

## Final Verification

- [ ] Run:

```bash
dotnet build
dotnet test
```

- [ ] Confirm `git status --short` is clean except for intentional uncommitted files.
- [ ] Summarize touched files and note that old custom plan tables remain in historical migrations/database until a later cleanup migration.
