using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Caching.Distributed;

namespace WiSave.Portal.Session;

public static class SessionConfiguration
{
    public static IServiceCollection AddPortalSession(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var redisConnection = configuration["Redis:ConnectionString"];

        if (!string.IsNullOrEmpty(redisConnection))
        {
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redisConnection;
                options.InstanceName = "WiSave:";
            });
        }
        else
        {
            services.AddDistributedMemoryCache();
        }
        
        services.AddSingleton<ITicketStore>(sp => new RedisTicketStore(sp.GetRequiredService<IDistributedCache>()));

        services.AddOptions<CookieAuthenticationOptions>(
                Microsoft.AspNetCore.Identity.IdentityConstants.ApplicationScheme)
            .Configure<ITicketStore>((options, store) =>
            {
                options.SessionStore = store;
            });

        return services;
    }
}
