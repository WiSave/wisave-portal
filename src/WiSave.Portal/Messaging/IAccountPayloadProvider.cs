using WiSave.Portal.Hubs.Realtime;

namespace WiSave.Portal.Messaging;

public interface IAccountPayloadProvider
{
    Task<AccountPayload> GetAsync(string userId, string accountId, CancellationToken ct = default);
}
