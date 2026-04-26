using MassTransit;
using Microsoft.AspNetCore.SignalR;
using NSubstitute;
using WiSave.Expenses.Contracts.Events;
using WiSave.Expenses.Contracts.Events.Accounts;
using WiSave.Expenses.Contracts.Events.Expenses;
using WiSave.Expenses.Contracts.Models;
using WiSave.Portal.Hubs;
using WiSave.Portal.Hubs.Realtime;
using WiSave.Portal.Messaging;
using Xunit;

namespace WiSave.Portal.UnitTests.Messaging;

public class NotificationConsumerEnvelopeTests
{
    [Fact]
    public async Task ExpenseRecorded_sent_as_realtimeEvent_envelope_with_entityId()
    {
        var (hub, clients, group) = CreateHub();
        var payloadProvider = Substitute.For<IAccountPayloadProvider>();
        var consumer = new NotificationConsumer(hub, payloadProvider);

        var userId = Guid.NewGuid().ToString();
        var expenseId = Guid.NewGuid().ToString();

        var msg = new ExpenseRecorded(
            ExpenseId: expenseId, UserId: userId, AccountId: "acc-1",
            CategoryId: "cat-1", SubcategoryId: null,
            Amount: 10m, Currency: Currency.PLN,
            Date: DateOnly.FromDateTime(DateTime.UtcNow), Description: "x",
            Recurring: false, Metadata: null, Timestamp: DateTimeOffset.UtcNow);

        var ctx = Substitute.For<ConsumeContext<ExpenseRecorded>>();
        ctx.Message.Returns(msg);
        ctx.CancellationToken.Returns(CancellationToken.None);

        await consumer.Consume(ctx);

        clients.Received().Group(userId);
        var env = CaptureSentEnvelope(group);
        Assert.Equal("expenses", env.Domain);
        Assert.Equal(RealtimeEventType.ExpenseRecorded, env.EventType);
        Assert.Equal(expenseId, env.EntityId);
    }

    [Fact]
    public async Task AccountOpened_sent_as_full_account_payload()
    {
        var (hub, _, group) = CreateHub();
        var payloadProvider = Substitute.For<IAccountPayloadProvider>();
        var consumer = new NotificationConsumer(hub, payloadProvider);

        var userId = Guid.NewGuid().ToString();
        var accountId = Guid.NewGuid().ToString();
        var payload = new AccountPayload(
            AccountId: accountId,
            UserId: userId,
            Name: "Millennium",
            Type: "CreditCard",
            Variant: null, Currency: "PLN",
            Balance: null,
            LinkedBankAccountId: "bank-1",
            CreditLimit: 5000m,
            BillingCycleDay: 16,
            PreviousCycleDebt: 1200m,
            CurrentCycleDebt: 340m,
            Color: "#f59e0b",
            LastFourDigits: "4532",
            Timestamp: DateTimeOffset.UtcNow);

        payloadProvider.GetAsync(userId, accountId, Arg.Any<CancellationToken>())
            .Returns(payload);

        var msg = new AccountOpened(
            AccountId: accountId, UserId: userId, Name: "Checking",
            Type: AccountType.BankAccount, Currency: Currency.PLN, Balance: 0m,
            LinkedBankAccountId: null, CreditLimit: null, BillingCycleDay: null,
            Color: null, LastFourDigits: null,
            Timestamp: DateTimeOffset.UtcNow);

        var ctx = Substitute.For<ConsumeContext<AccountOpened>>();
        ctx.Message.Returns(msg);
        ctx.CancellationToken.Returns(CancellationToken.None);

        await consumer.Consume(ctx);

        var env = CaptureSentEnvelope(group);
        Assert.Equal(RealtimeEventType.AccountOpened, env.EventType);
        Assert.Equal(accountId, env.EntityId);
        var actualPayload = Assert.IsType<AccountPayload>(env.Payload);
        Assert.Equal("CreditCard", actualPayload.Type);
        Assert.Equal(1200m, actualPayload.PreviousCycleDebt);
        Assert.Equal(340m, actualPayload.CurrentCycleDebt);
    }

    [Fact]
    public async Task AccountUpdated_sent_as_full_account_payload_not_patch()
    {
        var (hub, _, group) = CreateHub();
        var payloadProvider = Substitute.For<IAccountPayloadProvider>();
        var consumer = new NotificationConsumer(hub, payloadProvider);

        var userId = Guid.NewGuid().ToString();
        var accountId = Guid.NewGuid().ToString();
        var payload = new AccountPayload(
            AccountId: accountId,
            UserId: userId,
            Name: "Travel Card",
            Type: "DebitCard",
            Variant: "standalone",
            Currency: "EUR",
            Balance: 250m,
            LinkedBankAccountId: null,
            CreditLimit: null,
            BillingCycleDay: null,
            PreviousCycleDebt: null,
            CurrentCycleDebt: null,
            Color: null,
            LastFourDigits: "8812",
            Timestamp: DateTimeOffset.UtcNow);

        payloadProvider.GetAsync(userId, accountId, Arg.Any<CancellationToken>())
            .Returns(payload);

        var msg = new AccountUpdated(
            AccountId: accountId, UserId: userId, Name: "Travel Card",
            Type: AccountType.DebitCard, Currency: Currency.EUR, Balance: 250m,
            LinkedBankAccountId: null, CreditLimit: null, BillingCycleDay: null,
            Color: null, LastFourDigits: "8812",
            Timestamp: DateTimeOffset.UtcNow);

        var ctx = Substitute.For<ConsumeContext<AccountUpdated>>();
        ctx.Message.Returns(msg);
        ctx.CancellationToken.Returns(CancellationToken.None);

        await consumer.Consume(ctx);

        var env = CaptureSentEnvelope(group);
        Assert.Equal(RealtimeEventType.AccountUpdated, env.EventType);
        Assert.Equal(accountId, env.EntityId);
        var actualPayload = Assert.IsType<AccountPayload>(env.Payload);
        Assert.Equal("DebitCard", actualPayload.Type);
        Assert.Equal("standalone", actualPayload.Variant);
        Assert.Equal(250m, actualPayload.Balance);
    }

    [Fact]
    public async Task CommandFailed_sent_as_envelope_with_null_entityId()
    {
        var (hub, _, group) = CreateHub();
        var payloadProvider = Substitute.For<IAccountPayloadProvider>();
        var consumer = new NotificationConsumer(hub, payloadProvider);

        var userId = Guid.NewGuid().ToString();
        var msg = new CommandFailed(
            CorrelationId: Guid.CreateVersion7(),
            UserId: userId,
            CommandType: "RecordExpense",
            Reason: "validation",
            Timestamp: DateTimeOffset.UtcNow);

        var ctx = Substitute.For<ConsumeContext<CommandFailed>>();
        ctx.Message.Returns(msg);
        ctx.CancellationToken.Returns(CancellationToken.None);

        await consumer.Consume(ctx);

        var env = CaptureSentEnvelope(group);
        Assert.Equal(RealtimeEventType.CommandFailed, env.EventType);
        Assert.Null(env.EntityId);
    }

    private static (IHubContext<NotificationsHub>, IHubClients, IClientProxy) CreateHub()
    {
        var hub = Substitute.For<IHubContext<NotificationsHub>>();
        var clients = Substitute.For<IHubClients>();
        var group = Substitute.For<IClientProxy>();
        hub.Clients.Returns(clients);
        clients.Group(Arg.Any<string>()).Returns(group);
        return (hub, clients, group);
    }

    private static RealtimeEnvelope CaptureSentEnvelope(IClientProxy group)
    {
        var call = group.ReceivedCalls().First(c => c.GetMethodInfo().Name == nameof(IClientProxy.SendCoreAsync));
        var args = call.GetArguments();
        var methodName = (string)args[0]!;
        Assert.Equal("realtimeEvent", methodName);
        var messageArgs = (object?[])args[1]!;
        return (RealtimeEnvelope)messageArgs[0]!;
    }
}
