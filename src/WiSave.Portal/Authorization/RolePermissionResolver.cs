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
            foreach (var claim in claims.Where(c =>
                c.Type == PortalClaimTypes.Permission && !string.IsNullOrWhiteSpace(c.Value)))
            {
                permissions.Add(claim.Value);
            }
        }

        return permissions;
    }
}
