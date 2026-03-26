using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Caching.Distributed;

namespace WiSave.Portal.Session;

public class RedisTicketStore(IDistributedCache cache) : ITicketStore
{
    private const string KeyPrefix = "WiSave:AuthTicket:";

    public async Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        var key = Guid.NewGuid().ToString();
        await RenewAsync(key, ticket);
        return key;
    }

    public async Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        var bytes = TicketSerializer.Default.Serialize(ticket);
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpiration = ticket.Properties.ExpiresUtc
        };
        await cache.SetAsync(KeyPrefix + key, bytes, options);
    }

    public async Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        var bytes = await cache.GetAsync(KeyPrefix + key);
        return bytes is null ? null : TicketSerializer.Default.Deserialize(bytes);
    }

    public async Task RemoveAsync(string key)
    {
        await cache.RemoveAsync(KeyPrefix + key);
    }
}
