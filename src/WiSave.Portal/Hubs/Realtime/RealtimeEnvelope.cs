namespace WiSave.Portal.Hubs.Realtime;

public record RealtimeEnvelope(
    Guid EventId,
    string Domain,
    string EventType,
    DateTime OccurredAt,
    string? EntityId,
    object Payload);
