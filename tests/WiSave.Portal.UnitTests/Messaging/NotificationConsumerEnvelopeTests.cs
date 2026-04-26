using MassTransit;
using Microsoft.AspNetCore.SignalR;
using NSubstitute;
using WiSave.Expenses.Contracts.Events;
using WiSave.Expenses.Contracts.Events.Expenses;
using WiSave.Expenses.Contracts.Events.FundingAccounts;
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
        var payloadProvider = Substitute.For<IExpensesRealtimePayloadProvider>();
        var consumer = new NotificationConsumer(hub, payloadProvider);

        var userId = Guid.NewGuid().ToString();
        var expenseId = Guid.NewGuid().ToString();

        var msg = new ExpenseRecorded(
            ExpenseId: expenseId, UserId: userId, AccountId: "funding-1",
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
    public async Task FundingAccountOpened_sent_as_full_funding_account_payload()
    {
        var (hub, _, group) = CreateHub();
        var payloadProvider = Substitute.For<IExpensesRealtimePayloadProvider>();
        var consumer = new NotificationConsumer(hub, payloadProvider);

        var userId = Guid.NewGuid().ToString();
        var fundingAccountId = Guid.NewGuid().ToString();
        var payload = new FundingAccountPayload(
            FundingAccountId: fundingAccountId,
            UserId: userId,
            Name: "Main bank",
            Kind: "BankAccount",
            Currency: "PLN",
            Balance: 1200m,
            Color: "#2563eb",
            Timestamp: DateTimeOffset.UtcNow);

        payloadProvider.GetFundingAccountAsync(userId, fundingAccountId, Arg.Any<CancellationToken>())
            .Returns(payload);

        var msg = new FundingAccountOpened(
            FundingAccountId: fundingAccountId,
            UserId: userId,
            Name: "Main bank",
            Kind: FundingAccountKind.BankAccount,
            Currency: Currency.PLN,
            OpeningBalance: 1200m,
            Color: "#2563eb",
            Timestamp: DateTimeOffset.UtcNow);

        var ctx = Substitute.For<ConsumeContext<FundingAccountOpened>>();
        ctx.Message.Returns(msg);
        ctx.CancellationToken.Returns(CancellationToken.None);

        await consumer.Consume(ctx);

        var env = CaptureSentEnvelope(group);
        Assert.Equal(RealtimeEventType.FundingAccountOpened, env.EventType);
        Assert.Equal(fundingAccountId, env.EntityId);
        var actualPayload = Assert.IsType<FundingAccountPayload>(env.Payload);
        Assert.Equal("BankAccount", actualPayload.Kind);
        Assert.Equal(1200m, actualPayload.Balance);
    }

    [Fact]
    public async Task FundingTransferPosted_sent_as_transfer_event_with_funding_account_entityId()
    {
        var (hub, _, group) = CreateHub();
        var payloadProvider = Substitute.For<IExpensesRealtimePayloadProvider>();
        var consumer = new NotificationConsumer(hub, payloadProvider);

        var userId = Guid.NewGuid().ToString();
        var msg = new FundingTransferPosted(
            FundingAccountId: "funding-1",
            UserId: userId,
            TransferId: "transfer-1",
            null,
            null,
            Amount: 500m,
            PostedAtUtc: DateTimeOffset.UtcNow,
            Timestamp: DateTimeOffset.UtcNow);

        var ctx = Substitute.For<ConsumeContext<FundingTransferPosted>>();
        ctx.Message.Returns(msg);
        ctx.CancellationToken.Returns(CancellationToken.None);

        await consumer.Consume(ctx);

        var env = CaptureSentEnvelope(group);
        Assert.Equal(RealtimeEventType.FundingTransferPosted, env.EventType);
        Assert.Equal("funding-1", env.EntityId);
    }

    [Fact]
    public async Task CommandFailed_sent_as_envelope_with_new_command_name()
    {
        var (hub, _, group) = CreateHub();
        var payloadProvider = Substitute.For<IExpensesRealtimePayloadProvider>();
        var consumer = new NotificationConsumer(hub, payloadProvider);

        var userId = Guid.NewGuid().ToString();
        var msg = new CommandFailed(
            CorrelationId: Guid.CreateVersion7(),
            UserId: userId,
            CommandType: "PostFundingTransfer",
            Reason: "validation",
            Timestamp: DateTimeOffset.UtcNow);

        var ctx = Substitute.For<ConsumeContext<CommandFailed>>();
        ctx.Message.Returns(msg);
        ctx.CancellationToken.Returns(CancellationToken.None);

        await consumer.Consume(ctx);

        var env = CaptureSentEnvelope(group);
        Assert.Equal(RealtimeEventType.CommandFailed, env.EventType);
        Assert.Null(env.EntityId);
        var actualPayload = Assert.IsType<CommandFailed>(env.Payload);
        Assert.Equal("PostFundingTransfer", actualPayload.CommandType);
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
