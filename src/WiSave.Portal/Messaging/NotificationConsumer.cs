using MassTransit;
using Microsoft.AspNetCore.SignalR;
using WiSave.Expenses.Contracts.Events;
using WiSave.Expenses.Contracts.Events.Budgets;
using WiSave.Expenses.Contracts.Events.CreditCards;
using WiSave.Expenses.Contracts.Events.Expenses;
using WiSave.Expenses.Contracts.Events.FundingAccounts;
using WiSave.Portal.Hubs;
using WiSave.Portal.Hubs.Realtime;

namespace WiSave.Portal.Messaging;

public class NotificationConsumer(
    IHubContext<NotificationsHub> hub,
    IExpensesRealtimePayloadProvider payloadProvider) :
    IConsumer<FundingAccountOpened>,
    IConsumer<FundingAccountUpdated>,
    IConsumer<FundingAccountClosed>,
    IConsumer<FundingPaymentInstrumentAdded>,
    IConsumer<FundingPaymentInstrumentUpdated>,
    IConsumer<FundingPaymentInstrumentRemoved>,
    IConsumer<FundingTransferPosted>,
    IConsumer<CreditCardAccountOpened>,
    IConsumer<CreditCardAccountUpdated>,
    IConsumer<CreditCardAccountClosed>,
    IConsumer<CreditCardStateSeeded>,
    IConsumer<CreditCardStatementIssued>,
    IConsumer<CreditCardStatementPaymentApplied>,
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
    public async Task Consume(ConsumeContext<FundingAccountOpened> ctx)
    {
        var payload = await payloadProvider.GetFundingAccountAsync(
            ctx.Message.UserId,
            ctx.Message.FundingAccountId,
            ctx.CancellationToken);
        await Push(
            RealtimeEventType.FundingAccountOpened,
            ctx.Message.UserId,
            ctx.Message.FundingAccountId,
            payload,
            ctx.CancellationToken);
    }

    public async Task Consume(ConsumeContext<FundingAccountUpdated> ctx)
    {
        var payload = await payloadProvider.GetFundingAccountAsync(
            ctx.Message.UserId,
            ctx.Message.FundingAccountId,
            ctx.CancellationToken);
        await Push(
            RealtimeEventType.FundingAccountUpdated,
            ctx.Message.UserId,
            ctx.Message.FundingAccountId,
            payload,
            ctx.CancellationToken);
    }

    public Task Consume(ConsumeContext<FundingAccountClosed> ctx) =>
        Push(ctx, RealtimeEventType.FundingAccountClosed, ctx.Message.UserId, ctx.Message.FundingAccountId);

    public Task Consume(ConsumeContext<FundingPaymentInstrumentAdded> ctx) =>
        Push(ctx, RealtimeEventType.FundingPaymentInstrumentAdded, ctx.Message.UserId, ctx.Message.PaymentInstrumentId);

    public Task Consume(ConsumeContext<FundingPaymentInstrumentUpdated> ctx) =>
        Push(ctx, RealtimeEventType.FundingPaymentInstrumentUpdated, ctx.Message.UserId, ctx.Message.PaymentInstrumentId);

    public Task Consume(ConsumeContext<FundingPaymentInstrumentRemoved> ctx) =>
        Push(ctx, RealtimeEventType.FundingPaymentInstrumentRemoved, ctx.Message.UserId, ctx.Message.PaymentInstrumentId);

    public Task Consume(ConsumeContext<FundingTransferPosted> ctx) =>
        Push(ctx, RealtimeEventType.FundingTransferPosted, ctx.Message.UserId, ctx.Message.FundingAccountId);

    public async Task Consume(ConsumeContext<CreditCardAccountOpened> ctx)
    {
        var payload = await payloadProvider.GetCreditCardAccountAsync(
            ctx.Message.UserId,
            ctx.Message.CreditCardAccountId,
            ctx.CancellationToken);
        await Push(
            RealtimeEventType.CreditCardAccountOpened,
            ctx.Message.UserId,
            ctx.Message.CreditCardAccountId,
            payload,
            ctx.CancellationToken);
    }

    public async Task Consume(ConsumeContext<CreditCardAccountUpdated> ctx)
    {
        var payload = await payloadProvider.GetCreditCardAccountAsync(
            ctx.Message.UserId,
            ctx.Message.CreditCardAccountId,
            ctx.CancellationToken);
        await Push(
            RealtimeEventType.CreditCardAccountUpdated,
            ctx.Message.UserId,
            ctx.Message.CreditCardAccountId,
            payload,
            ctx.CancellationToken);
    }

    public Task Consume(ConsumeContext<CreditCardAccountClosed> ctx) =>
        Push(ctx, RealtimeEventType.CreditCardAccountClosed, ctx.Message.UserId, ctx.Message.CreditCardAccountId);

    public Task Consume(ConsumeContext<CreditCardStateSeeded> ctx) =>
        Push(ctx, RealtimeEventType.CreditCardStateSeeded, userId: null, ctx.Message.CreditCardAccountId);

    public Task Consume(ConsumeContext<CreditCardStatementIssued> ctx) =>
        Push(ctx, RealtimeEventType.CreditCardStatementIssued, userId: null, ctx.Message.CreditCardAccountId);

    public Task Consume(ConsumeContext<CreditCardStatementPaymentApplied> ctx) =>
        Push(ctx, RealtimeEventType.CreditCardStatementPaymentApplied, userId: null, ctx.Message.CreditCardAccountId);

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

    private Task Push<T>(ConsumeContext<T> ctx, string eventType, string? userId, string? entityId)
        where T : class =>
        Push(eventType, userId, entityId, ctx.Message!, ctx.CancellationToken);

    private Task Push(
        string eventType,
        string? userId,
        string? entityId,
        object payload,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return Task.CompletedTask;

        var env = new RealtimeEnvelope(
            EventId: Guid.CreateVersion7(),
            Domain: "expenses",
            EventType: eventType,
            OccurredAt: DateTime.UtcNow,
            EntityId: entityId,
            Payload: payload);
        return hub.Clients.Group(userId).SendAsync("realtimeEvent", env, cancellationToken);
    }
}
