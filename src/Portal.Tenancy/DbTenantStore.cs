using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Portal.Tenancy.Data;

namespace Portal.Tenancy;

public sealed class DbTenantStore(TenancyDb db, IMemoryCache cache) : ITenantStore
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(2);

    public Task<TenantInfo?> FindByIdentifierAsync(string identifier, CancellationToken ct = default)
    {
        var key = identifier.Trim().ToLowerInvariant();
        return cache.GetOrCreateAsync($"tenant:identifier:{key}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = Ttl;
            var t = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.IsActive && x.Identifier == key);
            return Map(t);
        });
    }

    public Task<TenantInfo?> FindByIdAsync(Guid id, CancellationToken ct = default) =>
        cache.GetOrCreateAsync($"tenant:id:{id}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = Ttl;
            var t = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.IsActive && x.Id == id);
            return Map(t);
        });

    private static TenantInfo? Map(TenantEntity? t) =>
        t is null ? null : new TenantInfo(t.Id, t.Identifier, t.Name, t.Isolation, t.ConnectionString);
}
