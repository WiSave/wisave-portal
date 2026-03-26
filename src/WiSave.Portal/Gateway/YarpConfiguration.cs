namespace WiSave.Portal.Gateway;

public static class YarpConfiguration
{
    public static IServiceCollection AddPortalGateway(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddReverseProxy()
            .LoadFromConfig(configuration.GetSection("ReverseProxy"))
            .AddTransforms<UserHeaderTransformProvider>();

        return services;
    }
}
