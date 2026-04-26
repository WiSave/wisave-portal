using System.Text.Json;
using WiSave.Portal.Hubs.Realtime;
using Xunit;

namespace WiSave.Portal.UnitTests.Hubs.Realtime;

public class RealtimeEnvelopeTests
{
    [Fact]
    public void Envelope_serializes_with_camelCase_contract_fields()
    {
        var env = new RealtimeEnvelope(
            EventId: Guid.CreateVersion7(),
            Domain: "expenses",
            EventType: RealtimeEventType.ExpenseRecorded,
            OccurredAt: new DateTime(2026, 4, 16, 12, 0, 0, DateTimeKind.Utc),
            EntityId: "expense-123",
            Payload: new { amount = 100 });

        var json = JsonSerializer.Serialize(env, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        Assert.Contains("\"eventId\"", json);
        Assert.Contains("\"domain\":\"expenses\"", json);
        Assert.Contains("\"eventType\":\"expense.recorded\"", json);
        Assert.Contains("\"entityId\":\"expense-123\"", json);
        Assert.Contains("\"payload\"", json);
        Assert.DoesNotContain("\"correlationId\"", json);
    }

    [Fact]
    public void Envelope_allows_null_entityId()
    {
        var env = new RealtimeEnvelope(
            EventId: Guid.CreateVersion7(),
            Domain: "expenses",
            EventType: RealtimeEventType.CommandFailed,
            OccurredAt: DateTime.UtcNow,
            EntityId: null,
            Payload: new { });

        Assert.Null(env.EntityId);
    }
}
