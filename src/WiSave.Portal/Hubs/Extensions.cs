using Microsoft.Extensions.Configuration;
using StackExchange.Redis;

namespace WiSave.Portal.Hubs;

public static class Extensions
{
    public static IServiceCollection AddPortalSignalR(this IServiceCollection services, IConfiguration configuration)
    {
        var builder = services.AddSignalR();

        var redisConnection = configuration["Redis:ConnectionString"];
        if (!string.IsNullOrWhiteSpace(redisConnection))
        {
            builder.AddStackExchangeRedis(redisConnection, options =>
            {
                options.Configuration.ChannelPrefix = RedisChannel.Literal("WiSave.SignalR");
            });
        }

        return services;
    }

    public static WebApplication MapPortalHubs(this WebApplication app)
    {
        app.MapHub<NotificationsHub>("/hubs/notifications");
        return app;
    }
}
