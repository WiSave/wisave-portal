using Wolverine;
using Wolverine.RabbitMQ;
using WiSave.Expenses.Contracts.Events;
using WiSave.Framework.Messaging.Wolverine;
using WiSave.Incomes.Contracts.Events;
using WiSave.Portal.EventHandlers;

namespace WiSave.Portal.Messaging;

public static class Extensions
{
    private static readonly BrokerName IncomesBroker = new("incomes");
    private static readonly BrokerName ExpensesBroker = new("expenses");

    public static IHostApplicationBuilder AddPortalMessaging(this IHostApplicationBuilder builder)
    {
        var configuration = builder.Configuration;
        var rabbitMqSettings = configuration.GetRabbitMqBusSettings("portal");
        var incomesRabbitMqSettings = configuration.GetNamedRabbitMqBrokerSettings("Incomes", "incomes", rabbitMqSettings);
        var expensesRabbitMqSettings = configuration.GetNamedRabbitMqBrokerSettings("Expenses", "expenses", rabbitMqSettings);

        builder.UseWolverine(options =>
        {
            options.Discovery.IncludeType<NotificationsEventHandler>();
            
            options.UseRabbitMq(rabbit =>
                {
                    rabbit.ConfigureRabbitMq(rabbitMqSettings);
                })
                .EnableEnhancedDeadLettering()
                .AutoProvision();

            options.AddNamedRabbitMqBroker(IncomesBroker, rabbit =>
                {
                    rabbit.ConfigureRabbitMq(incomesRabbitMqSettings);
                })
                .EnableEnhancedDeadLettering()
                .AutoProvision();

            options.ListenToEventOnNamedBroker<CategoryCreated, NotificationsEventHandler>(IncomesBroker);

            options.AddNamedRabbitMqBroker(ExpensesBroker, rabbit =>
                {
                    rabbit.ConfigureRabbitMq(expensesRabbitMqSettings);
                })
                .EnableEnhancedDeadLettering()
                .AutoProvision();

            options.ListenToEventOnNamedBroker<ExpenseCreated, NotificationsEventHandler>(ExpensesBroker);
        });

        return builder;
    }
}
