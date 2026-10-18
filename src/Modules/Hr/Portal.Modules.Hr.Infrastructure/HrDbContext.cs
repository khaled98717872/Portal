using Microsoft.EntityFrameworkCore;
using Portal.Modules.Hr.Domain;
using Portal.SharedKernel.Auditing;
using Portal.SharedKernel.Tenancy;
using Portal.Tenancy.Data;

namespace Portal.Modules.Hr.Infrastructure;

public sealed class HrDbContext(DbContextOptions<HrDbContext> options, ITenantContext tenant, ICurrentUser currentUser)
    : TenantDbContext(options, tenant, currentUser)
{
    public const string Schema = "hr";

    public DbSet<Employee> Employees => Set<Employee>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Employee>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.FullName).HasMaxLength(200).IsRequired();
            e.Property(x => x.Department).HasMaxLength(100);
            e.HasIndex(x => x.TenantId);
        });

        base.OnModelCreating(modelBuilder);
    }
}
