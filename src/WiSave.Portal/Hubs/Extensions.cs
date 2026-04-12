namespace WiSave.Portal.Hubs;

public static class Extensions
{
    public static IServiceCollection AddPortalSignalR(this IServiceCollection services)
    {
        services.AddSignalR();
        return services;
    }

    public static WebApplication MapPortalHubs(this WebApplication app)
    {
        app.MapHub<NotificationsHub>("/hubs/notifications");
        return app;
    }
}