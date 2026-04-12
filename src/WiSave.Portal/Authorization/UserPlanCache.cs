using Microsoft.Extensions.Caching.Distributed;
using WiSave.Portal.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace WiSave.Portal.Authorization;

public class UserPlanCache(IDistributedCache cache, IServiceScopeFactory scopeFactory)
{
    private const string KeyPrefix = "user:plan:";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);

    public async Task<string?> GetPlanIdAsync(string userId)
    {
        var cached = await cache.GetStringAsync(KeyPrefix + userId);
        if (cached is not null)
            return cached;

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();

        var planId = await db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.PlanId)
            .FirstOrDefaultAsync();

        if (planId is not null)
        {
            await cache.SetStringAsync(KeyPrefix + userId, planId, new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = CacheTtl
            });
        }

        return planId;
    }

    public async Task InvalidateAsync(string userId)
    {
        await cache.RemoveAsync(KeyPrefix + userId);
    }
}
