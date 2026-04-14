using MassTransit;
using WiSave.Expenses.Contracts.Bus;
using WiSave.Portal.Contracts.Bus;

namespace WiSave.Portal.Messaging;

public static class Extensions
{
    public static IServiceCollection AddPortalMessaging(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var useInMemoryTransport = UseInMemoryTransport(configuration);
        var expensesSettings = GetBusSettings(configuration, "Expenses", "expenses");
        var portalSettings = GetBusSettings(configuration, "Portal", "portal");

        services.AddMassTransit<IExpensesBus>(x =>
        {
            x.AddConsumer<NotificationConsumer>();
            x.SetEndpointNameFormatter(new DefaultEndpointNameFormatter(".", null, true));
            ConfigureTransport(x, useInMemoryTransport, expensesSettings);
        });

        services.AddMassTransit<IPortalBus>(x =>
        {
            x.SetEndpointNameFormatter(new DefaultEndpointNameFormatter(".", null, true));
            ConfigureTransport(x, useInMemoryTransport, portalSettings);
        });

        return services;
    }

    private static void ConfigureTransport<TBus>(
        IBusRegistrationConfigurator<TBus> configurator,
        bool useInMemoryTransport,
        RabbitMqBusSettings settings)
        where TBus : class, IBus
    {
        if (useInMemoryTransport)
        {
            configurator.UsingInMemory((context, cfg) =>
            {
                cfg.ConfigureEndpoints(context);
            });

            return;
        }

        configurator.UsingRabbitMq((context, cfg) =>
        {
            cfg.Host(settings.Host, settings.VirtualHost, h =>
            {
                h.Username(settings.Username);
                h.Password(settings.Password);
            });

            cfg.ConfigureEndpoints(context);
        });
    }

    private static bool UseInMemoryTransport(IConfiguration configuration)
    {
        var transport = configuration["Messaging:Transport"];
        if (string.Equals(transport, "InMemory", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.IsNullOrWhiteSpace(configuration["RabbitMq:Host"]);
    }

    private static RabbitMqBusSettings GetBusSettings(
        IConfiguration configuration,
        string sectionName,
        string defaultVirtualHost)
    {
        var section = configuration.GetSection($"RabbitMq:{sectionName}");

        return new RabbitMqBusSettings(
            Host: section["Host"] ?? configuration["RabbitMq:Host"] ?? "localhost",
            VirtualHost: section["VirtualHost"] ?? configuration["RabbitMq:VirtualHost"] ?? defaultVirtualHost,
            Username: section["Username"] ?? configuration["RabbitMq:Username"] ?? "guest",
            Password: section["Password"] ?? configuration["RabbitMq:Password"] ?? "guest");
    }

    private sealed record RabbitMqBusSettings(
        string Host,
        string VirtualHost,
        string Username,
        string Password);
}
