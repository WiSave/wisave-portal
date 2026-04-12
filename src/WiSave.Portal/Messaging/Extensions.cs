using MassTransit;

namespace WiSave.Portal.Messaging;

public static class Extensions
{
    public static IServiceCollection AddPortalMessaging(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var rabbitMqHost = configuration["RabbitMq:Host"] ?? "localhost";
        var virtualHost = configuration["RabbitMq:VirtualHost"] ?? "expenses";
        var username = configuration["RabbitMq:Username"] ?? "guest";
        var password = configuration["RabbitMq:Password"] ?? "guest";

        services.AddMassTransit(x =>
        {
            x.AddConsumer<NotificationConsumer>();
            x.SetEndpointNameFormatter(new DefaultEndpointNameFormatter(".", null, true));
            x.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(rabbitMqHost, virtualHost, h =>
                {
                    h.Username(username);
                    h.Password(password);
                });

                cfg.ConfigureEndpoints(context);
            });
        });

        return services;
    }
}
