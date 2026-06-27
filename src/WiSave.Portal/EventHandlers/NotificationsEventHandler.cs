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
        return Push(
            RealtimeEventType.CategoryCreated,
            message.UserId.ToString(),
            message.Id.ToString(),
            message,
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
