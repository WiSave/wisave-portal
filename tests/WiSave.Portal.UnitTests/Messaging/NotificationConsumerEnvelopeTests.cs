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
        var consumer = new NotificationConsumer(hub);

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
    public async Task AccountOpened_sent_as_envelope_with_accountId_as_entityId()
    {
        var (hub, _, group) = CreateHub();
        var consumer = new NotificationConsumer(hub);

        var userId = Guid.NewGuid().ToString();
        var accountId = Guid.NewGuid().ToString();

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
    }

    [Fact]
    public async Task CommandFailed_sent_as_envelope_with_null_entityId()
    {
        var (hub, _, group) = CreateHub();
        var consumer = new NotificationConsumer(hub);

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
