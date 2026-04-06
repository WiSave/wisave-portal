using MassTransit;
using Microsoft.AspNetCore.SignalR;
using WiSave.Expenses.Contracts.Events;
using WiSave.Expenses.Contracts.Events.Accounts;
using WiSave.Expenses.Contracts.Events.Budgets;
using WiSave.Expenses.Contracts.Events.Expenses;
using WiSave.Portal.Hubs;

namespace WiSave.Portal.Messaging;

public class NotificationConsumer(IHubContext<NotificationsHub> hub) :
    IConsumer<AccountOpened>,
    IConsumer<AccountUpdated>,
    IConsumer<AccountClosed>,
    IConsumer<ExpenseRecorded>,
    IConsumer<ExpenseUpdated>,
    IConsumer<ExpenseDeleted>,
    IConsumer<BudgetCreated>,
    IConsumer<BudgetCopiedFromPrevious>,
    IConsumer<OverallLimitSet>,
    IConsumer<CategoryLimitSet>,
    IConsumer<CategoryLimitRemoved>,
    IConsumer<CommandFailed>
{
    public Task Consume(ConsumeContext<AccountOpened> context) =>
        Push(context.Message.UserId, nameof(AccountOpened), context.Message, context.CancellationToken);

    public Task Consume(ConsumeContext<AccountUpdated> context) =>
        Push(context.Message.UserId, nameof(AccountUpdated), context.Message, context.CancellationToken);

    public Task Consume(ConsumeContext<AccountClosed> context) =>
        Push(context.Message.UserId, nameof(AccountClosed), context.Message, context.CancellationToken);

    public Task Consume(ConsumeContext<ExpenseRecorded> context) =>
        Push(context.Message.UserId, nameof(ExpenseRecorded), context.Message, context.CancellationToken);

    public Task Consume(ConsumeContext<ExpenseUpdated> context) =>
        Push(context.Message.UserId, nameof(ExpenseUpdated), context.Message, context.CancellationToken);

    public Task Consume(ConsumeContext<ExpenseDeleted> context) =>
        Push(context.Message.UserId, nameof(ExpenseDeleted), context.Message, context.CancellationToken);

    public Task Consume(ConsumeContext<BudgetCreated> context) =>
        Push(context.Message.UserId, nameof(BudgetCreated), context.Message, context.CancellationToken);

    public Task Consume(ConsumeContext<BudgetCopiedFromPrevious> context) =>
        Push(context.Message.UserId, nameof(BudgetCopiedFromPrevious), context.Message, context.CancellationToken);

    public Task Consume(ConsumeContext<OverallLimitSet> context) =>
        Push(context.Message.UserId, nameof(OverallLimitSet), context.Message, context.CancellationToken);

    public Task Consume(ConsumeContext<CategoryLimitSet> context) =>
        Push(context.Message.UserId, nameof(CategoryLimitSet), context.Message, context.CancellationToken);

    public Task Consume(ConsumeContext<CategoryLimitRemoved> context) =>
        Push(context.Message.UserId, nameof(CategoryLimitRemoved), context.Message, context.CancellationToken);

    public Task Consume(ConsumeContext<CommandFailed> context) =>
        Push(context.Message.UserId, nameof(CommandFailed), context.Message, context.CancellationToken);

    private Task Push(string userId, string eventName, object message, CancellationToken cancellationToken) =>
        hub.Clients.Group(userId).SendAsync(eventName, message, cancellationToken);
}