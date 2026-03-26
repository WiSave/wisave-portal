using Microsoft.Extensions.Caching.Distributed;
using WiSave.Portal.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace WiSave.Portal.Authorization;

public class UserPlanCache
{
    private const string KeyPrefix = "user:plan:";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);

    private readonly IDistributedCache _cache;
    private readonly IServiceScopeFactory _scopeFactory;

    public UserPlanCache(IDistributedCache cache, IServiceScopeFactory scopeFactory)
    {
        _cache = cache;
        _scopeFactory = scopeFactory;
    }

    public async Task<string?> GetPlanIdAsync(string userId)
    {
        var cached = await _cache.GetStringAsync(KeyPrefix + userId);
        if (cached is not null)
            return cached;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();

        var planId = await db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.PlanId)
            .FirstOrDefaultAsync();

        if (planId is not null)
        {
            await _cache.SetStringAsync(KeyPrefix + userId, planId, new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = CacheTtl
            });
        }

        return planId;
    }

    public async Task InvalidateAsync(string userId)
    {
        await _cache.RemoveAsync(KeyPrefix + userId);
    }
}
