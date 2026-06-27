using Microsoft.AspNetCore.SignalR;
using WiSave.Expenses.Contracts.Events;
using WiSave.Incomes.Contracts.Events;
using WiSave.Portal.Hubs;
using WiSave.Portal.Hubs.Realtime;
using Wolverine.Attributes;

namespace WiSave.Portal.EventHandlers;

[StickyHandler(nameof(NotificationsEventHandler))]
public class NotificationsEventHandler(IHubContext<NotificationsHub> hub)
{
    public Task Handle(ExpenseCreated message, CancellationToken cancellationToken = default)
    {
        return Push(
            RealtimeEventType.ExpenseCreated,
            message.UserId.ToString(),
            message.Id.Value.ToString(),
            message,
            cancellationToken);
    }

    public Task Handle(CategoryCreated message, CancellationToken cancellationToken = default)
    {
        return PushIncome(RealtimeEventType.CategoryCreated, message.UserId, message.Id, message, cancellationToken);
    }

    public Task Handle(CategoryUpdated message, CancellationToken cancellationToken = default)
    {
        return PushIncome(RealtimeEventType.CategoryUpdated, message.UserId, message.Id, message, cancellationToken);
    }

    public Task Handle(CategoryDeleted message, CancellationToken cancellationToken = default)
    {
        return PushIncome(RealtimeEventType.CategoryDeleted, message.UserId, message.Id, message, cancellationToken);
    }

    public Task Handle(SubcategoryCreated message, CancellationToken cancellationToken = default)
    {
        return PushIncome(RealtimeEventType.SubcategoryCreated, message.UserId, message.Id, message, cancellationToken);
    }

    public Task Handle(SubcategoryUpdated message, CancellationToken cancellationToken = default)
    {
        return PushIncome(RealtimeEventType.SubcategoryUpdated, message.UserId, message.Id, message, cancellationToken);
    }

    public Task Handle(SubcategoryDeleted message, CancellationToken cancellationToken = default)
    {
        return PushIncome(RealtimeEventType.SubcategoryDeleted, message.UserId, message.Id, message, cancellationToken);
    }

    private Task PushIncome(
        string eventType,
        Guid userId,
        Guid entityId,
        object payload,
        CancellationToken cancellationToken)
    {
        return Push(
            eventType,
            userId.ToString(),
            entityId.ToString(),
            payload,
            cancellationToken,
            domain: "incomes");
    }

    private Task Push(
        string eventType,
        string? userId,
        string? entityId,
        object payload,
        CancellationToken cancellationToken,
        string domain = "expenses")
    {
        if (string.IsNullOrWhiteSpace(userId))
            return Task.CompletedTask;

        var env = new RealtimeEnvelope(
            EventId: Guid.CreateVersion7(),
            Domain: domain,
            EventType: eventType,
            OccurredAt: DateTime.UtcNow,
            EntityId: entityId,
            Payload: payload);
        return hub.Clients.Group(userId).SendAsync("realtimeEvent", env, cancellationToken);
    }
}
