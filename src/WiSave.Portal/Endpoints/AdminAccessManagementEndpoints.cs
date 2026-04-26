using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WiSave.Portal.Auth.Models;
using WiSave.Portal.Authorization;
using WiSave.Portal.Contracts.Authorization;
using WiSave.Portal.Filters;

namespace WiSave.Portal.Endpoints;

public static class AdminAccessManagementEndpoints
{
    public static void MapAdminAccessManagementEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/admin/access-management")
            .RequireAuthorization()
            .WithTags("Admin Access Management");

        group.MapGet("/", GetAccessManagement)
            .Produces<AccessManagementResponse>()
            .Produces(401)
            .Produces(403)
            .WithSummary("Get roles, permissions, users, and role assignment capabilities");

        group.MapPost("/roles", CreateRole)
            .AddEndpointFilter<AntiforgeryValidationFilter>()
            .Produces<AccessRoleResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(401)
            .Produces(403)
            .WithSummary("Create a custom role");

        group.MapPut("/users/{userId}/roles", UpdateUserRoles)
            .AddEndpointFilter<AntiforgeryValidationFilter>()
            .Produces<AccessUserResponse>()
            .ProducesValidationProblem()
            .Produces(401)
            .Produces(403)
            .Produces(404)
            .WithSummary("Update roles assigned to a user");

        group.MapPut("/roles/{roleId}/permissions", UpdateRolePermissions)
            .AddEndpointFilter<AntiforgeryValidationFilter>()
            .Produces<AccessRoleResponse>()
            .ProducesValidationProblem()
            .Produces(401)
            .Produces(403)
            .Produces(404)
            .WithSummary("Update permission claims assigned to a role");
    }

    private static async Task<IResult> GetAccessManagement(
        HttpContext context,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        RolePermissionResolver rolePermissionResolver)
    {
        var currentUser = await GetCurrentUserAsync(context, userManager);
        if (currentUser is null)
            return Results.Unauthorized();

        var currentUserRoles = await userManager.GetRolesAsync(currentUser);
        var canReadAccessManagement = CanReadAccessManagement(currentUserRoles);
        if (!canReadAccessManagement)
            return Results.Forbid();

        var canManagePrivilegedRoles = CanManagePrivilegedRoles(currentUserRoles);
        var roles = await GetRoleResponsesAsync(roleManager);
        var roleByName = roles.ToDictionary(role => role.Name, StringComparer.OrdinalIgnoreCase);
        var users = await userManager.Users.OrderBy(user => user.Email).ToListAsync();
        var userResponses = new List<AccessUserResponse>(users.Count);

        foreach (var user in users)
        {
            userResponses.Add(await BuildUserResponseAsync(
                user,
                userManager,
                rolePermissionResolver,
                roleByName,
                canManagePrivilegedRoles));
        }

        return Results.Ok(new AccessManagementResponse(canManagePrivilegedRoles, GetAvailablePermissions(), [.. roles], [.. userResponses]));
    }

    private static async Task<IResult> UpdateUserRoles(
        string userId,
        UpdateUserRolesRequest request,
        HttpContext context,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        RolePermissionResolver rolePermissionResolver)
    {
        var currentUser = await GetCurrentUserAsync(context, userManager);
        if (currentUser is null)
            return Results.Unauthorized();

        var currentUserRoles = await userManager.GetRolesAsync(currentUser);
        if (!CanReadAccessManagement(currentUserRoles))
            return Results.Forbid();

        var targetUser = await userManager.FindByIdAsync(userId);
        if (targetUser is null)
            return Results.NotFound();

        var requestedRoleIds = request.RoleIds
            .Where(roleId => !string.IsNullOrWhiteSpace(roleId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var allRoles = await roleManager.Roles.ToListAsync();
        var roleById = allRoles.ToDictionary(role => role.Id, StringComparer.OrdinalIgnoreCase);
        var missingRoleId = requestedRoleIds.FirstOrDefault(roleId => !roleById.ContainsKey(roleId));
        if (missingRoleId is not null)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(UpdateUserRolesRequest.RoleIds)] = [$"Role '{missingRoleId}' does not exist."]
            });

        if (requestedRoleIds.Length != 1)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(UpdateUserRolesRequest.RoleIds)] = ["Exactly one role is required."]
            });
        }

        var requestedRole = roleById[requestedRoleIds[0]];
        var canManagePrivilegedRoles = CanManagePrivilegedRoles(currentUserRoles);
        var targetRoles = await userManager.GetRolesAsync(targetUser);
        
        if (!canManagePrivilegedRoles && ContainsPrivilegedRole(targetRoles))
            return Results.Forbid();

        if (!canManagePrivilegedRoles && IsPrivilegedRole(requestedRole.Name))
            return Results.Forbid();

        var removeResult = await userManager.RemoveFromRolesAsync(targetUser, targetRoles);
        if (!removeResult.Succeeded)
            return Results.BadRequest(new { errors = removeResult.Errors.Select(error => error.Description) });

        var addResult = await userManager.AddToRoleAsync(targetUser, requestedRole.Name!);
        if (!addResult.Succeeded)
            return Results.BadRequest(new { errors = addResult.Errors.Select(error => error.Description) });

        var roleResponses = await GetRoleResponsesAsync(roleManager);
        var roleByName = roleResponses.ToDictionary(role => role.Name, StringComparer.OrdinalIgnoreCase);
        var response = await BuildUserResponseAsync(
            targetUser,
            userManager,
            rolePermissionResolver,
            roleByName,
            canManagePrivilegedRoles);

        return Results.Ok(response);
    }

    private static async Task<IResult> CreateRole(
        CreateRoleRequest request,
        HttpContext context,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager)
    {
        var currentUser = await GetCurrentUserAsync(context, userManager);
        if (currentUser is null)
            return Results.Unauthorized();

        var currentUserRoles = await userManager.GetRolesAsync(currentUser);
        if (!CanReadAccessManagement(currentUserRoles))
            return Results.Forbid();

        var roleName = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(roleName))
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(CreateRoleRequest.Name)] = ["Role name is required."] });

        if (IsReservedRoleName(roleName))
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(CreateRoleRequest.Name)] = ["This role name is reserved."] });

        if (await roleManager.RoleExistsAsync(roleName))
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(CreateRoleRequest.Name)] = ["Role already exists."] });

        var requestedPermissions = NormalizePermissionRequest(request.Permissions);
        var invalidPermission = FindInvalidPermission(requestedPermissions);
        if (invalidPermission is not null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(CreateRoleRequest.Permissions)] = [$"Permission '{invalidPermission}' does not exist."]
            });
        }

        var role = new IdentityRole(roleName);
        var createResult = await roleManager.CreateAsync(role);
        if (!createResult.Succeeded)
            return Results.BadRequest(new { errors = createResult.Errors.Select(error => error.Description) });

        foreach (var permission in requestedPermissions)
        {
            var claimResult = await roleManager.AddClaimAsync(role, new Claim(PortalClaimTypes.Permission, permission));
            if (!claimResult.Succeeded)
                return Results.BadRequest(new { errors = claimResult.Errors.Select(error => error.Description) });
        }

        var response = await BuildRoleResponseAsync(roleManager, role);
        return Results.Created($"/api/admin/access-management/roles/{role.Id}", response);
    }

    private static async Task<IResult> UpdateRolePermissions(
        string roleId,
        UpdateRolePermissionsRequest request,
        HttpContext context,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager)
    {
        var currentUser = await GetCurrentUserAsync(context, userManager);
        if (currentUser is null)
            return Results.Unauthorized();

        var currentUserRoles = await userManager.GetRolesAsync(currentUser);
        if (!CanReadAccessManagement(currentUserRoles))
            return Results.Forbid();

        var role = await roleManager.FindByIdAsync(roleId);
        if (role is null)
            return Results.NotFound();

        var requestedPermissions = NormalizePermissionRequest(request.Permissions);
        var invalidPermission = FindInvalidPermission(requestedPermissions);
        if (invalidPermission is not null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(UpdateRolePermissionsRequest.Permissions)] = [$"Permission '{invalidPermission}' does not exist."]
            });
        }

        var existingClaims = await roleManager.GetClaimsAsync(role);
        foreach (var claim in existingClaims.Where(claim => claim.Type == PortalClaimTypes.Permission))
        {
            var result = await roleManager.RemoveClaimAsync(role, claim);
            if (!result.Succeeded)
                return Results.BadRequest(new { errors = result.Errors.Select(error => error.Description) });
        }

        foreach (var permission in requestedPermissions)
        {
            var result = await roleManager.AddClaimAsync(role, new Claim(PortalClaimTypes.Permission, permission));
            if (!result.Succeeded)
                return Results.BadRequest(new { errors = result.Errors.Select(error => error.Description) });
        }

        return Results.Ok(await BuildRoleResponseAsync(roleManager, role));
    }

    private static async Task<ApplicationUser?> GetCurrentUserAsync(HttpContext context, UserManager<ApplicationUser> userManager)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return userId is null ? null : await userManager.FindByIdAsync(userId);
    }

    private static async Task<List<AccessRoleResponse>> GetRoleResponsesAsync(RoleManager<IdentityRole> roleManager)
    {
        var roles = await roleManager.Roles.OrderBy(role => role.Name).ToListAsync();
        var responses = new List<AccessRoleResponse>(roles.Count);

        foreach (var role in roles)
        {
            responses.Add(await BuildRoleResponseAsync(roleManager, role));
        }

        return responses;
    }

    private static async Task<AccessRoleResponse> BuildRoleResponseAsync(
        RoleManager<IdentityRole> roleManager,
        IdentityRole role)
    {
        var claims = await roleManager.GetClaimsAsync(role);
        var permissions = claims
            .Where(claim => claim.Type == PortalClaimTypes.Permission && !string.IsNullOrWhiteSpace(claim.Value))
            .Select(claim => claim.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new AccessRoleResponse(
            role.Id,
            role.Name!,
            role.NormalizedName!,
            role.ConcurrencyStamp,
            permissions);
    }

    private static async Task<AccessUserResponse> BuildUserResponseAsync(
        ApplicationUser user,
        UserManager<ApplicationUser> userManager,
        RolePermissionResolver rolePermissionResolver,
        IReadOnlyDictionary<string, AccessRoleResponse> roleByName,
        bool canManagePrivilegedRoles)
    {
        var userRoles = await userManager.GetRolesAsync(user);
        var roleIds = userRoles
            .Select(roleName => roleByName.TryGetValue(roleName, out var role) ? role.Id : null)
            .Where(roleId => roleId is not null)
            .Cast<string>()
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var permissions = await rolePermissionResolver.GetPermissionsAsync(user);
        var canEditRoles = canManagePrivilegedRoles || !ContainsPrivilegedRole(userRoles);

        return new AccessUserResponse(
            user.Id,
            user.Name,
            user.Email!,
            roleIds,
            [.. permissions.Order(StringComparer.OrdinalIgnoreCase)],
            canEditRoles);
    }

    private static bool CanReadAccessManagement(IEnumerable<string> roles) =>
        roles.Any(IsPrivilegedRole);

    private static bool CanManagePrivilegedRoles(IEnumerable<string> roles) =>
        roles.Contains(PortalRoles.SuperAdmin, StringComparer.OrdinalIgnoreCase);

    private static bool ContainsPrivilegedRole(IEnumerable<string> roles) =>
        roles.Any(IsPrivilegedRole);

    private static bool IsPrivilegedRole(string? role) =>
        string.Equals(role, PortalRoles.Admin, StringComparison.OrdinalIgnoreCase)
        || string.Equals(role, PortalRoles.SuperAdmin, StringComparison.OrdinalIgnoreCase);

    private static bool IsReservedRoleName(string role) =>
        role.StartsWith("plan:", StringComparison.OrdinalIgnoreCase)
        || IsPrivilegedRole(role);

    private static string[] NormalizePermissionRequest(IEnumerable<string> permissions) =>
        permissions
            .Where(permission => !string.IsNullOrWhiteSpace(permission))
            .Select(permission => permission.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string? FindInvalidPermission(IEnumerable<string> permissions)
    {
        var availablePermissions = GetAvailablePermissions().ToHashSet(StringComparer.OrdinalIgnoreCase);
        return permissions.FirstOrDefault(permission => !availablePermissions.Contains(permission));
    }

    private static string[] GetAvailablePermissions() =>
    [
        PortalPermissions.Expenses.Read,
        PortalPermissions.Expenses.Write,
        PortalPermissions.Expenses.Delete,
        PortalPermissions.Incomes.Read,
        PortalPermissions.Incomes.Write,
        PortalPermissions.Incomes.Delete,
        PortalPermissions.Incomes.Import,
        PortalPermissions.Stocks.Read,
        PortalPermissions.Stocks.Write,
        PortalPermissions.Stocks.PortfolioManage,
        PortalPermissions.Stocks.WatchlistManage
    ];
}

public sealed record AccessManagementResponse(
    bool CanManagePrivilegedRoles,
    string[] AvailablePermissions,
    AccessRoleResponse[] Roles,
    AccessUserResponse[] Users);

public sealed record AccessRoleResponse(
    string Id,
    string Name,
    string NormalizedName,
    string? ConcurrencyStamp,
    string[] Permissions);

public sealed record AccessUserResponse(
    string Id,
    string Name,
    string Email,
    string[] Roles,
    string[] Permissions,
    bool CanEditRoles);

public sealed record UpdateUserRolesRequest(string[] RoleIds);

public sealed record UpdateRolePermissionsRequest(string[] Permissions);

public sealed record CreateRoleRequest(string Name, string[] Permissions);
