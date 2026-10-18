using Microsoft.EntityFrameworkCore;

namespace Portal.Tenancy.Data;

public sealed class TenancyDb(DbContextOptions<TenancyDb> options) : DbContext(options)
{
    public const string Schema = "tenancy";

    public DbSet<TenantEntity> Tenants => Set<TenantEntity>();
    public DbSet<TenantModule> TenantModules => Set<TenantModule>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<TenantEntity>(e =>
        {
            e.ToTable("Tenants");
            e.HasKey(x => x.Id);
            e.Property(x => x.Identifier).HasMaxLength(63).IsRequired();
            e.HasIndex(x => x.Identifier).IsUnique();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.ConnectionString).HasMaxLength(1000);
            e.HasMany(x => x.Modules).WithOne().HasForeignKey(m => m.TenantId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TenantModule>(e =>
        {
            e.ToTable("TenantModules");
            e.HasKey(x => new { x.TenantId, x.ModuleName });
            e.Property(x => x.ModuleName).HasMaxLength(100).IsRequired();
        });
    }
}
