using Microsoft.AspNetCore.Authorization;

namespace WiSave.Portal.Authorization;

public sealed class PermissionHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.Resource is not HttpContext httpContext)
        {
            return Task.CompletedTask;
        }

        if (httpContext.Items["UserPermissions"] is not IReadOnlySet<string> permissions)
        {
            return Task.CompletedTask;
        }

        if (permissions.Contains("*"))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        var prefix = requirement.PermissionPrefix + ":";
        if (permissions.Any(p => p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
