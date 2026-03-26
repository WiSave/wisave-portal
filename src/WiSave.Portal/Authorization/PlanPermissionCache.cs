using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using WiSave.Portal.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace WiSave.Portal.Authorization;

public class PlanPermissionCache(IDistributedCache cache, IServiceScopeFactory scopeFactory)
{
    private const string KeyPrefix = "permissions:plan:";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);

    public async Task<IReadOnlySet<string>> GetPermissionsAsync(string planId)
    {
        var cached = await cache.GetStringAsync(KeyPrefix + planId);
        if (cached is not null)
        {
            var deserialized = JsonSerializer.Deserialize<string[]>(cached);
            return deserialized?.ToHashSet() ?? new HashSet<string>();
        }

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();

        var permissions = await db.PlanPermissions
            .Where(pp => pp.PlanId == planId)
            .Select(pp => pp.Permission.Name)
            .ToListAsync();

        var permissionSet = permissions.ToHashSet();

        await cache.SetStringAsync(KeyPrefix + planId, JsonSerializer.Serialize(permissions),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = CacheTtl
            });

        return permissionSet;
    }

    public async Task InvalidateAsync(string planId)
    {
        await cache.RemoveAsync(KeyPrefix + planId);
    }
}
