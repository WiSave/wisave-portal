using System.Security.Claims;

namespace WiSave.Portal.Authorization;

public class PermissionResolutionMiddleware(RequestDelegate next)
{
    private static readonly HashSet<string> AdminRoles = ["superadmin", "admin"];

    public async Task InvokeAsync(HttpContext context, UserPlanCache userPlanCache, PlanPermissionCache planPermissionCache)
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
        
        var roles = context.User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToHashSet();
        if (roles.Overlaps(AdminRoles))
        {
            context.Items["UserPermissions"] = new HashSet<string> { "*" };
            await next(context);
            return;
        }
        
        var planId = await userPlanCache.GetPlanIdAsync(userId);
        if (planId is null)
        {
            await next(context);
            return;
        }

        var permissions = await planPermissionCache.GetPermissionsAsync(planId);
        context.Items["UserPermissions"] = permissions;

        await next(context);
    }
}
