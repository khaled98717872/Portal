using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Portal.SharedKernel.Authorization;
using Portal.SharedKernel.Tenancy;
using Portal.Tenancy.Data;

namespace Portal.Tenancy;

public sealed record TenantModuleRow(string Name, bool IsEnabled, DateTimeOffset? ExpiresAt);

public sealed record TenantRow(
    Guid Id, string Identifier, string Name, TenantIsolation Isolation, bool IsActive,
    IReadOnlyList<TenantModuleRow> Modules);

public sealed class ModuleSelection
{
    public string Name { get; set; } = "";
    public bool Enabled { get; set; }
    public DateTime? ExpiresOn { get; set; }
}

public sealed class TenantEditModel
{
    public Guid? Id { get; set; }
    public string Identifier { get; set; } = "";
    public string Name { get; set; } = "";
    public TenantIsolation Isolation { get; set; } = TenantIsolation.SharedDatabase;
    public string? ConnectionString { get; set; }
    public bool IsActive { get; set; } = true;
    public List<ModuleSelection> Modules { get; set; } = [];
}

public sealed class TenantAdminService(
    DbContextOptions<TenancyDb> options,
    IMemoryCache cache,
    AuthenticationStateProvider auth,
    IAuthorizationService authorization)
{
    private static readonly Regex IdentifierRegex = new("^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?$", RegexOptions.Compiled);
    private static readonly HashSet<string> Reserved =
        new(new[] { "platform", "www", "api", "admin", "identity" }, StringComparer.OrdinalIgnoreCase);

    private TenancyDb NewDb() => new(options);

    private async Task EnsureAllowedAsync()
    {
        var state = await auth.GetAuthenticationStateAsync();
        var result = await authorization.AuthorizeAsync(state.User, PlatformPermissions.TenantsManage);
        if (!result.Succeeded) throw new UnauthorizedAccessException("You are not allowed to manage tenants.");
    }

    public async Task<List<TenantRow>> ListAsync(CancellationToken ct = default)
    {
        await EnsureAllowedAsync();
        await using var db = NewDb();

        var tenants = await db.Tenants.AsNoTracking()
            .Include(t => t.Modules)
            .OrderBy(t => t.Identifier)
            .ToListAsync(ct);

        return tenants.Select(t => new TenantRow(
            t.Id, t.Identifier, t.Name, t.Isolation, t.IsActive,
            t.Modules.OrderBy(m => m.ModuleName)
                .Select(m => new TenantModuleRow(m.ModuleName, m.IsEnabled, m.ExpiresAt))
                .ToList())).ToList();
    }

    public async Task<TenantEditModel> GetEditModelAsync(
        Guid id, IEnumerable<string> knownModules, CancellationToken ct = default)
    {
        await EnsureAllowedAsync();
        await using var db = NewDb();

        var t = await db.Tenants.AsNoTracking().Include(x => x.Modules)
                    .FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new InvalidOperationException("Tenant not found.");

        return new TenantEditModel
        {
            Id = t.Id,
            Identifier = t.Identifier,
            Name = t.Name,
            Isolation = t.Isolation,
            ConnectionString = t.ConnectionString,
            IsActive = t.IsActive,
            Modules = knownModules.Select(name =>
            {
                var row = t.Modules.FirstOrDefault(m => m.ModuleName.Equals(name, StringComparison.OrdinalIgnoreCase));
                return new ModuleSelection
                {
                    Name = name,
                    Enabled = row is { IsEnabled: true },
                    ExpiresOn = row?.ExpiresAt is { } e ? e.UtcDateTime.AddDays(-1).Date : null
                };
            }).ToList()
        };
    }

    public async Task<Guid> SaveAsync(
        TenantEditModel model, IReadOnlySet<string> knownModules, CancellationToken ct = default)
    {
        await EnsureAllowedAsync();

        var identifier = model.Identifier.Trim().ToLowerInvariant();
        var name = model.Name.Trim();

        if (!IdentifierRegex.IsMatch(identifier))
            throw new InvalidOperationException("Identifier: up to 63 characters, lowercase letters, digits and hyphens only.");
        if (name.Length == 0)
            throw new InvalidOperationException("Name is required.");
        if (model.Isolation == TenantIsolation.DedicatedDatabase && string.IsNullOrWhiteSpace(model.ConnectionString))
            throw new InvalidOperationException("A connection string is required for a dedicated database.");

        await using var db = NewDb();

        TenantEntity tenant;
        string? oldIdentifier = null;

        if (model.Id is { } id)
        {
            tenant = await db.Tenants.Include(t => t.Modules).FirstOrDefaultAsync(t => t.Id == id, ct)
                     ?? throw new InvalidOperationException("Tenant not found.");

            if (tenant.Id == PlatformTenant.Id)
                throw new InvalidOperationException("The platform tenant cannot be modified.");
            if (tenant.Isolation != model.Isolation)
                throw new InvalidOperationException("Isolation mode cannot be changed after creation (existing data would not move).");
            if (Reserved.Contains(identifier) && !tenant.Identifier.Equals(identifier, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"'{identifier}' is a reserved identifier.");

            oldIdentifier = tenant.Identifier;
        }
        else
        {
            if (Reserved.Contains(identifier))
                throw new InvalidOperationException($"'{identifier}' is a reserved identifier.");

            tenant = new TenantEntity();
            db.Tenants.Add(tenant);
        }

        if (await db.Tenants.AnyAsync(t => t.Identifier == identifier && t.Id != tenant.Id, ct))
            throw new InvalidOperationException($"Identifier '{identifier}' is already used.");

        tenant.Identifier = identifier;
        tenant.Name = name;
        tenant.Isolation = model.Isolation;
        tenant.ConnectionString = model.Isolation == TenantIsolation.DedicatedDatabase
            ? model.ConnectionString!.Trim()
            : null;
        tenant.IsActive = model.IsActive;

        foreach (var selection in model.Modules.Where(s => knownModules.Contains(s.Name)))
        {
            var existing = tenant.Modules.FirstOrDefault(
                m => m.ModuleName.Equals(selection.Name, StringComparison.OrdinalIgnoreCase));

            if (!selection.Enabled)
            {
                if (existing is not null) db.TenantModules.Remove(existing);
                continue;
            }

            // The selected date is inclusive: the subscription ends at the start of the next UTC day.
            DateTimeOffset? expires = selection.ExpiresOn is { } d
                ? new DateTimeOffset(DateTime.SpecifyKind(d.Date.AddDays(1), DateTimeKind.Utc))
                : null;

            if (existing is null)
                tenant.Modules.Add(new TenantModule { ModuleName = selection.Name, IsEnabled = true, ExpiresAt = expires });
            else
            {
                existing.IsEnabled = true;
                existing.ExpiresAt = expires;
            }
        }

        await db.SaveChangesAsync(ct);
        Invalidate(tenant.Id, oldIdentifier, identifier);
        return tenant.Id;
    }

    public async Task SetActiveAsync(Guid id, bool active, CancellationToken ct = default)
    {
        await EnsureAllowedAsync();
        if (id == PlatformTenant.Id)
            throw new InvalidOperationException("The platform tenant cannot be modified.");

        await using var db = NewDb();
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct)
                     ?? throw new InvalidOperationException("Tenant not found.");

        tenant.IsActive = active;
        await db.SaveChangesAsync(ct);
        Invalidate(id, tenant.Identifier);
    }

    // Keys must match DbTenantStore and ModuleAccessService.
    private void Invalidate(Guid id, params string?[] identifiers)
    {
        cache.Remove($"tenant:id:{id}");
        cache.Remove($"modules:{id}");
        foreach (var identifier in identifiers.Where(i => !string.IsNullOrWhiteSpace(i)))
            cache.Remove($"tenant:identifier:{identifier!.Trim().ToLowerInvariant()}");
    }
}
