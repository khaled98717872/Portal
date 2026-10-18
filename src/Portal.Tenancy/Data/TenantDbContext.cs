using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Portal.SharedKernel.Auditing;
using Portal.SharedKernel.Tenancy;

namespace Portal.Tenancy.Data;

public abstract class TenantDbContext(DbContextOptions options, ITenantContext tenant, ICurrentUser currentUser)
    : DbContext(options)
{
    private static readonly MethodInfo FilterMethod =
        typeof(TenantDbContext).GetMethod(nameof(ApplyTenantFilter), BindingFlags.NonPublic | BindingFlags.Instance)!;

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    private Guid CurrentTenantId => tenant.IsResolved ? tenant.TenantId : Guid.Empty;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AuditEntry>(e =>
        {
            e.ToTable("AuditEntries");
            e.HasKey(x => x.Id);
            e.Property(x => x.Action).HasMaxLength(20).IsRequired();
            e.Property(x => x.EntityType).HasMaxLength(200).IsRequired();
            e.Property(x => x.EntityId).HasMaxLength(100).IsRequired();
            e.Property(x => x.UserId).HasMaxLength(100);
            e.Property(x => x.UserName).HasMaxLength(256);
            e.Property(x => x.TraceId).HasMaxLength(100);
            e.Property(x => x.Changes).HasColumnType("jsonb");
            e.HasIndex(x => new { x.TenantId, x.Timestamp });
            e.HasIndex(x => new { x.EntityType, x.EntityId });
        });

        foreach (var entityType in modelBuilder.Model.GetEntityTypes()
                     .Where(t => typeof(ITenantScoped).IsAssignableFrom(t.ClrType)))
        {
            FilterMethod.MakeGenericMethod(entityType.ClrType).Invoke(this, [modelBuilder]);
        }
    }

    private void ApplyTenantFilter<T>(ModelBuilder modelBuilder) where T : class, ITenantScoped =>
        modelBuilder.Entity<T>().HasQueryFilter(e => e.TenantId == CurrentTenantId);

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampTenant();
        AddAuditEntries();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampTenant();
        AddAuditEntries();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void StampTenant()
    {
        foreach (var entry in ChangeTracker.Entries<ITenantScoped>())
        {
            if (entry.State == EntityState.Added)
            {
                if (!tenant.IsResolved)
                    throw new InvalidOperationException("Cannot save tenant data without a resolved tenant.");
                entry.Entity.TenantId = tenant.TenantId;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Property(e => e.TenantId).IsModified = false;
            }
        }
    }

    private void AddAuditEntries()
    {
        if (!tenant.IsResolved) return;

        ChangeTracker.DetectChanges();

        var now = DateTimeOffset.UtcNow;
        var traceId = Activity.Current?.TraceId.ToString();
        var audits = new List<AuditEntry>();

        foreach (var entry in ChangeTracker.Entries<ITenantScoped>())
        {
            if (entry.Entity is AuditEntry) continue;
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)) continue;

            var changes = new Dictionary<string, Dictionary<string, object?>>();

            foreach (var property in entry.Properties)
            {
                var meta = property.Metadata;
                if (meta.Name == nameof(ITenantScoped.TenantId)) continue;
                if (meta.PropertyInfo?.IsDefined(typeof(AuditIgnoreAttribute), inherit: true) == true) continue;

                switch (entry.State)
                {
                    case EntityState.Added:
                        changes[meta.Name] = new() { ["new"] = property.CurrentValue };
                        break;
                    case EntityState.Deleted:
                        changes[meta.Name] = new() { ["old"] = property.OriginalValue };
                        break;
                    case EntityState.Modified when property.IsModified && !Equals(property.OriginalValue, property.CurrentValue):
                        changes[meta.Name] = new() { ["old"] = property.OriginalValue, ["new"] = property.CurrentValue };
                        break;
                }
            }

            if (entry.State == EntityState.Modified && changes.Count == 0) continue;

            var key = entry.Metadata.FindPrimaryKey();
            var entityId = key is null
                ? ""
                : string.Join("|", key.Properties.Select(p => entry.Property(p.Name).CurrentValue?.ToString()));

            audits.Add(new AuditEntry
            {
                TenantId = tenant.TenantId,
                Timestamp = now,
                Action = entry.State switch
                {
                    EntityState.Added => AuditActions.Create,
                    EntityState.Deleted => AuditActions.Delete,
                    _ => AuditActions.Update
                },
                EntityType = entry.Metadata.ClrType.Name,
                EntityId = entityId,
                UserId = currentUser.Id,
                UserName = currentUser.Name,
                TraceId = traceId,
                Changes = JsonSerializer.Serialize(changes)
            });
        }

        if (audits.Count > 0) AuditEntries.AddRange(audits);
    }
}
