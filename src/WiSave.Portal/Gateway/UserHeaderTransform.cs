using System.Security.Claims;
using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace WiSave.Portal.Gateway;

public class UserHeaderTransformProvider : ITransformProvider
{
    public void ValidateRoute(TransformRouteValidationContext context) { }

    public void ValidateCluster(TransformClusterValidationContext context) { }

    public void Apply(TransformBuilderContext context)
    {
        context.AddRequestTransform(transformContext =>
        {
            // Strip any client-sent identity/permission headers
            transformContext.ProxyRequest.Headers.Remove("X-User-Id");
            transformContext.ProxyRequest.Headers.Remove("X-User-Email");
            transformContext.ProxyRequest.Headers.Remove("X-User-Roles");
            transformContext.ProxyRequest.Headers.Remove("X-User-Permissions");

            var user = transformContext.HttpContext.User;

            if (user.Identity?.IsAuthenticated == true)
            {
                var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
                var email = user.FindFirstValue(ClaimTypes.Email);
                var roles = string.Join(",",
                    user.FindAll(ClaimTypes.Role).Select(c => c.Value));

                if (userId is not null)
                    transformContext.ProxyRequest.Headers.TryAddWithoutValidation("X-User-Id", userId);
                if (email is not null)
                    transformContext.ProxyRequest.Headers.TryAddWithoutValidation("X-User-Email", email);
                if (!string.IsNullOrEmpty(roles))
                    transformContext.ProxyRequest.Headers.TryAddWithoutValidation("X-User-Roles", roles);

                // Inject permissions resolved by PermissionResolutionMiddleware
                if (transformContext.HttpContext.Items["UserPermissions"] is IReadOnlySet<string> permissions)
                {
                    transformContext.ProxyRequest.Headers.TryAddWithoutValidation(
                        "X-User-Permissions", string.Join(",", permissions));
                }
            }

            return ValueTask.CompletedTask;
        });
    }
}
