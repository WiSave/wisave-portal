using System.Security.Claims;
using WiSave.Portal.Contracts.Identity;
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
            transformContext.ProxyRequest.Headers.Remove(PortalHeaderNames.UserId);
            transformContext.ProxyRequest.Headers.Remove(PortalHeaderNames.UserEmail);
            transformContext.ProxyRequest.Headers.Remove(PortalHeaderNames.UserRoles);
            transformContext.ProxyRequest.Headers.Remove(PortalHeaderNames.UserPermissions);

            var user = transformContext.HttpContext.User;

            if (user.Identity?.IsAuthenticated == true)
            {
                var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
                if (userId is not null)
                {
                    var forwardedContext = new ForwardedUserContext(
                        userId,
                        user.FindFirstValue(ClaimTypes.Email),
                        transformContext.HttpContext.Items["UserPermissions"] as IReadOnlySet<string>
                            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                        user.FindAll(ClaimTypes.Role)
                            .Select(static claim => claim.Value)
                            .ToHashSet(StringComparer.OrdinalIgnoreCase));

                    foreach (var header in ForwardedUserContextWriter.Write(forwardedContext))
                    {
                        transformContext.ProxyRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    }
                }
            }

            return ValueTask.CompletedTask;
        });
    }
}
