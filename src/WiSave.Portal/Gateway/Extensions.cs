using Microsoft.AspNetCore.Antiforgery;

namespace WiSave.Portal.Gateway;

public static class Extensions
{
    private static readonly string[] UnsafeMethods = ["POST", "PUT", "DELETE", "PATCH"];

    public static WebApplication MapPortalReverseProxy(this WebApplication app)
    {
        app.MapReverseProxy(proxyPipeline =>
        {
            proxyPipeline.Use(async (context, next) =>
            {
                if (UnsafeMethods.Contains(context.Request.Method, StringComparer.OrdinalIgnoreCase))
                {
                    var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();
                    try
                    {
                        await antiforgery.ValidateRequestAsync(context);
                    }
                    catch (AntiforgeryValidationException)
                    {
                        context.Response.StatusCode = 400;
                        await context.Response.WriteAsync("Antiforgery token validation failed");
                        return;
                    }
                }

                await next();
            });
            proxyPipeline.UseSessionAffinity();
            proxyPipeline.UseLoadBalancing();
            proxyPipeline.UsePassiveHealthChecks();
        });

        return app;
    }
}
