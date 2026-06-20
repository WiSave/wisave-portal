using Microsoft.AspNetCore.SignalR;
using WiSave.Expenses.Contracts.Events;
using WiSave.Portal.Hubs;
using WiSave.Portal.Hubs.Realtime;

namespace WiSave.Portal.Messaging;

public class NotificationConsumer(
    IHubContext<NotificationsHub> hub)
{
    public Task Handle(ExpenseCreated message, CancellationToken cancellationToken = default) =>
        Push(
            RealtimeEventType.ExpenseCreated,
            message.UserId.ToString(),
            message.Id.Value.ToString(),
            message,
            cancellationToken);

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
