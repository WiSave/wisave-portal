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
