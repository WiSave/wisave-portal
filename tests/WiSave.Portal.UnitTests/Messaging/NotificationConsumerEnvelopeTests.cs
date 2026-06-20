using Microsoft.AspNetCore.SignalR;
using NSubstitute;
using WiSave.Expenses.Contracts.Events;
using WiSave.Expenses.Contracts.Models;
using WiSave.Portal.Hubs;
using WiSave.Portal.Hubs.Realtime;
using WiSave.Portal.Messaging;
using Xunit;

namespace WiSave.Portal.UnitTests.Messaging;

public class NotificationConsumerEnvelopeTests
{
    [Fact]
    public async Task ExpenseCreated_sent_as_realtimeEvent_envelope_with_entityId()
    {
        var (hub, clients, group) = CreateHub();
        var consumer = new NotificationConsumer(hub);

        var userId = Guid.NewGuid();
        var expenseId = Guid.NewGuid();
        var msg = new ExpenseCreated(
            Id: new ExpenseId(expenseId),
            Amount: new Money(10m, Currency.PLN),
            ExpenseDate: DateOnly.FromDateTime(DateTime.UtcNow),
            Name: "Lunch",
            Description: "Team lunch",
            UserId: userId,
            Tags: ["food"]);

        await consumer.Handle(msg, CancellationToken.None);

        clients.Received().Group(userId.ToString());
        var env = CaptureSentEnvelope(group);
        Assert.Equal("expenses", env.Domain);
        Assert.Equal(RealtimeEventType.ExpenseCreated, env.EventType);
        Assert.Equal(expenseId.ToString(), env.EntityId);
        Assert.Same(msg, env.Payload);
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
