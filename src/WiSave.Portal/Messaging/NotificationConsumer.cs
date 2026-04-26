using MassTransit;
using Microsoft.AspNetCore.SignalR;
using WiSave.Expenses.Contracts.Events;
using WiSave.Expenses.Contracts.Events.Accounts;
using WiSave.Expenses.Contracts.Events.Budgets;
using WiSave.Expenses.Contracts.Events.Expenses;
using WiSave.Portal.Hubs;
using WiSave.Portal.Hubs.Realtime;

namespace WiSave.Portal.Messaging;

public class NotificationConsumer(
    IHubContext<NotificationsHub> hub,
    IAccountPayloadProvider accountPayloadProvider) :
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
    public async Task Consume(ConsumeContext<AccountOpened> ctx)
    {
        var payload = await accountPayloadProvider.GetAsync(ctx.Message.UserId, ctx.Message.AccountId, ctx.CancellationToken);
        await PushAccount(RealtimeEventType.AccountOpened, payload, ctx.CancellationToken);
    }

    public async Task Consume(ConsumeContext<AccountUpdated> ctx)
    {
        var payload = await accountPayloadProvider.GetAsync(ctx.Message.UserId, ctx.Message.AccountId, ctx.CancellationToken);
        await PushAccount(RealtimeEventType.AccountUpdated, payload, ctx.CancellationToken);
    }

    public Task Consume(ConsumeContext<AccountClosed> ctx) =>
        Push(ctx, RealtimeEventType.AccountClosed, ctx.Message.UserId, ctx.Message.AccountId);

    public Task Consume(ConsumeContext<ExpenseRecorded> ctx) =>
        Push(ctx, RealtimeEventType.ExpenseRecorded, ctx.Message.UserId, ctx.Message.ExpenseId);

    public Task Consume(ConsumeContext<ExpenseUpdated> ctx) =>
        Push(ctx, RealtimeEventType.ExpenseUpdated, ctx.Message.UserId, ctx.Message.ExpenseId);

    public Task Consume(ConsumeContext<ExpenseDeleted> ctx) =>
        Push(ctx, RealtimeEventType.ExpenseDeleted, ctx.Message.UserId, ctx.Message.ExpenseId);

    public Task Consume(ConsumeContext<BudgetCreated> ctx) =>
        Push(ctx, RealtimeEventType.BudgetCreated, ctx.Message.UserId, ctx.Message.BudgetId);

    public Task Consume(ConsumeContext<BudgetCopiedFromPrevious> ctx) =>
        Push(ctx, RealtimeEventType.BudgetCopiedFromPrevious, ctx.Message.UserId, ctx.Message.BudgetId);

    public Task Consume(ConsumeContext<OverallLimitSet> ctx) =>
        Push(ctx, RealtimeEventType.OverallLimitSet, ctx.Message.UserId, ctx.Message.BudgetId);

    public Task Consume(ConsumeContext<CategoryLimitSet> ctx) =>
        Push(ctx, RealtimeEventType.CategoryLimitSet, ctx.Message.UserId, ctx.Message.BudgetId);

    public Task Consume(ConsumeContext<CategoryLimitRemoved> ctx) =>
        Push(ctx, RealtimeEventType.CategoryLimitRemoved, ctx.Message.UserId, ctx.Message.BudgetId);

    public Task Consume(ConsumeContext<CommandFailed> ctx) =>
        Push(ctx, RealtimeEventType.CommandFailed, ctx.Message.UserId, entityId: null);

    private Task PushAccount(string eventType, AccountPayload payload, CancellationToken cancellationToken)
    {
        var env = new RealtimeEnvelope(
            EventId: Guid.CreateVersion7(),
            Domain: "expenses",
            EventType: eventType,
            OccurredAt: DateTime.UtcNow,
            EntityId: payload.AccountId,
            Payload: payload);
        return hub.Clients.Group(payload.UserId).SendAsync("realtimeEvent", env, cancellationToken);
    }

    private Task Push<T>(ConsumeContext<T> ctx, string eventType, string userId, string? entityId)
        where T : class
    {
        var env = new RealtimeEnvelope(
            EventId: Guid.CreateVersion7(),
            Domain: "expenses",
            EventType: eventType,
            OccurredAt: DateTime.UtcNow,
            EntityId: entityId,
            Payload: ctx.Message!);
        return hub.Clients.Group(userId).SendAsync("realtimeEvent", env, ctx.CancellationToken);
    }
}
