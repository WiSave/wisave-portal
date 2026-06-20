using Wolverine;
using Wolverine.RabbitMQ;

namespace WiSave.Portal.Messaging;

public static class Extensions
{
    public static IHostApplicationBuilder AddPortalMessaging(this IHostApplicationBuilder builder)
    {
        var configuration = builder.Configuration;
        var useInMemoryTransport = UseInMemoryTransport(configuration);
        var rabbitMqSettings = GetBusSettings(configuration, "expenses");

        builder.Services.AddHttpClient<IExpensesRealtimePayloadProvider, ExpensesRealtimePayloadProvider>((_, client) =>
        {
            client.BaseAddress = GetExpensesBaseAddress(configuration);
        });

        builder.UseWolverine(options =>
        {
            options.Discovery.IncludeType<NotificationConsumer>();

            if (useInMemoryTransport)
            {
                options.PublishAllMessages().ToLocalQueue("portal-messaging");
                return;
            }

            options.UseRabbitMq(rabbit =>
                {
                    rabbit.HostName = rabbitMqSettings.Host;
                    rabbit.VirtualHost = rabbitMqSettings.VirtualHost;
                    rabbit.UserName = rabbitMqSettings.Username;
                    rabbit.Password = rabbitMqSettings.Password;
                })
                .EnableEnhancedDeadLettering()
                .AutoProvision()
                .UseConventionalRouting();
        });

        return builder;
    }

    private static bool UseInMemoryTransport(IConfiguration configuration)
    {
        var transport = configuration["Messaging:Transport"];
        if (string.Equals(transport, "InMemory", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(configuration["UseInMemoryDatabase"], "true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.IsNullOrWhiteSpace(configuration["RabbitMq:Host"]);
    }

    private static Uri GetExpensesBaseAddress(IConfiguration configuration)
    {
        var address = configuration
            .GetSection("ReverseProxy:Clusters:expenses-cluster:Destinations")
            .GetChildren()
            .Select(d => d["Address"])
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        if (string.IsNullOrWhiteSpace(address))
            throw new InvalidOperationException("ReverseProxy expenses-cluster destination is not configured.");

        return new Uri(address, UriKind.Absolute);
    }

    private static RabbitMqBusSettings GetBusSettings(IConfiguration configuration, string defaultVirtualHost)
    {
        return new RabbitMqBusSettings(
            Host: configuration["RabbitMq:Host"] ?? "localhost",
            VirtualHost: configuration["RabbitMq:VirtualHost"] ?? defaultVirtualHost,
            Username: configuration["RabbitMq:Username"] ?? "guest",
            Password: configuration["RabbitMq:Password"] ?? "guest");
    }

    private sealed record RabbitMqBusSettings(
        string Host,
        string VirtualHost,
        string Username,
        string Password);
}
