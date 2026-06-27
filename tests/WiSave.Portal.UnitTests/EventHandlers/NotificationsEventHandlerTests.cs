using Microsoft.AspNetCore.SignalR;
using NSubstitute;
using WiSave.Incomes.Contracts.Events;
using WiSave.Portal.EventHandlers;
using WiSave.Portal.Hubs;
using WiSave.Portal.Hubs.Realtime;
using Xunit;

namespace WiSave.Portal.UnitTests.EventHandlers;

public sealed class NotificationsEventHandlerTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CategoryId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SubcategoryId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    public static TheoryData<object, string, Guid> IncomeCategoryEvents()
    {
        return new TheoryData<object, string, Guid>
        {
            {
                new CategoryUpdated(CategoryId, UserId, "Salary", 10),
                "category.updated",
                CategoryId
            },
            {
                new CategoryDeleted(CategoryId, UserId),
                "category.deleted",
                CategoryId
            },
            {
                new SubcategoryCreated(SubcategoryId, CategoryId, UserId, "Base pay", 20),
                "subcategory.created",
                SubcategoryId
            },
            {
                new SubcategoryUpdated(CategoryId, SubcategoryId, UserId, "Bonus", 30),
                "subcategory.updated",
                SubcategoryId
            },
            {
                new SubcategoryDeleted(CategoryId, SubcategoryId, UserId),
                "subcategory.deleted",
                SubcategoryId
            }
        };
    }

    [Theory]
    [MemberData(nameof(IncomeCategoryEvents))]
    public async Task Handle_PublishesIncomeCategoryRealtimeEventToUserGroup(
        object message,
        string expectedEventType,
        Guid expectedEntityId)
    {
        var cancellationToken = new CancellationTokenSource().Token;
        var clientProxy = Substitute.For<IClientProxy>();
        var hubClients = Substitute.For<IHubClients>();
        var hub = Substitute.For<IHubContext<NotificationsHub>>();
        hub.Clients.Returns(hubClients);
        hubClients.Group(UserId.ToString()).Returns(clientProxy);

        var handler = new NotificationsEventHandler(hub);

        await Handle(handler, message, cancellationToken);

        await clientProxy.Received(1).SendCoreAsync(
            "realtimeEvent",
            Arg.Is<object[]>(arguments => ContainsExpectedEnvelope(arguments, expectedEventType, expectedEntityId)),
            cancellationToken);
    }

    private static Task Handle(NotificationsEventHandler handler, object message, CancellationToken cancellationToken)
    {
        return message switch
        {
            CategoryUpdated categoryUpdated => handler.Handle(categoryUpdated, cancellationToken),
            CategoryDeleted categoryDeleted => handler.Handle(categoryDeleted, cancellationToken),
            SubcategoryCreated subcategoryCreated => handler.Handle(subcategoryCreated, cancellationToken),
            SubcategoryUpdated subcategoryUpdated => handler.Handle(subcategoryUpdated, cancellationToken),
            SubcategoryDeleted subcategoryDeleted => handler.Handle(subcategoryDeleted, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(message), message, "Unsupported message type.")
        };
    }

    private static bool ContainsExpectedEnvelope(object[] arguments, string expectedEventType, Guid expectedEntityId)
    {
        Assert.Single(arguments);
        var envelope = Assert.IsType<RealtimeEnvelope>(arguments[0]);

        Assert.Equal("incomes", envelope.Domain);
        Assert.Equal(expectedEventType, envelope.EventType);
        Assert.Equal(expectedEntityId.ToString(), envelope.EntityId);
        Assert.NotNull(envelope.Payload);

        return true;
    }
}
