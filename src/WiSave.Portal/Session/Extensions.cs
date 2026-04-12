using System.IO;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.AspNetCore.Identity;
using StackExchange.Redis;

namespace WiSave.Portal.Session;

public static class Extensions
{
    public static IServiceCollection AddPortalSession(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<PortalSessionOptions>(configuration.GetSection("Session"));

        var redisConnection = configuration["Redis:ConnectionString"];
        var allowInMemoryFallback =
            configuration.GetValue<bool>("UseInMemoryDatabase") ||
            configuration.GetValue<bool>("Session:AllowInMemoryTicketStoreFallback");
        var dataProtection = services.AddDataProtection()
            .SetApplicationName(configuration["DataProtection:ApplicationName"] ?? "WiSave.Portal");

        if (!string.IsNullOrWhiteSpace(redisConnection))
        {
            var multiplexer = new Lazy<IConnectionMultiplexer>(() => ConnectionMultiplexer.Connect(redisConnection));
            services.AddSingleton<IConnectionMultiplexer>(_ => multiplexer.Value);
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redisConnection;
                options.InstanceName = "WiSave:";
            });
            dataProtection.PersistKeysToStackExchangeRedis(
                () => multiplexer.Value.GetDatabase(),
                configuration["DataProtection:RedisKey"] ?? "WiSave.Portal:DataProtection-Keys");
        }
        else if (allowInMemoryFallback)
        {
            services.AddDistributedMemoryCache();
            var keyRingPath = configuration["DataProtection:KeyRingPath"];
            if (string.IsNullOrWhiteSpace(keyRingPath))
            {
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                keyRingPath = Path.Combine(localAppData, "WiSave", "Portal", "DataProtection-Keys");
            }

            Directory.CreateDirectory(keyRingPath);
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));
        }
        else
        {
            throw new InvalidOperationException(
                "Redis:ConnectionString is required for authentication ticket storage. " +
                "Set Session:AllowInMemoryTicketStoreFallback=true only for local single-instance development.");
        }
        
        services.AddSingleton<ITicketStore>(sp => new RedisTicketStore(sp.GetRequiredService<IDistributedCache>()));

        services.AddOptions<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme)
            .Configure<ITicketStore>((options, store) =>
            {
                options.SessionStore = store;
            });

        return services;
    }
}
