using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Portal.SharedKernel.Tenancy;
using Portal.Tenancy.Data;

namespace Portal.Tenancy;

public static class TenancySeeder
{
    public static async Task SeedAsync(IServiceProvider sp, IConfiguration config)
    {
        var db = sp.GetRequiredService<TenancyDb>();

        if (!await db.Tenants.AnyAsync(t => t.Id == PlatformTenant.Id))
        {
            db.Tenants.Add(new TenantEntity
            {
                Id = PlatformTenant.Id,
                Identifier = PlatformTenant.Identifier,
                Name = "Platform",
                Isolation = TenantIsolation.SharedDatabase
            });
            await db.SaveChangesAsync();
        }

        if (await db.Tenants.AnyAsync(t => t.Id != PlatformTenant.Id)) return;

        var mohConnection = config["Seed:MohConnectionString"]
            ?? throw new InvalidOperationException("Seed:MohConnectionString is missing (use user-secrets).");

        db.Tenants.AddRange(
            new TenantEntity
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Identifier = "moh", Name = "Ministry (sample)",
                Isolation = TenantIsolation.DedicatedDatabase, ConnectionString = mohConnection,
                Modules = { new TenantModule { ModuleName = "Hr" }, new TenantModule { ModuleName = "Documents" } }
            },
            new TenantEntity
            {
                Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                Identifier = "acme", Name = "Acme Co (sample)",
                Isolation = TenantIsolation.SharedDatabase,
                Modules = { new TenantModule { ModuleName = "Hr" }, new TenantModule { ModuleName = "Documents" } }
            },
            new TenantEntity
            {
                Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                Identifier = "beta", Name = "Beta Co (sample)",
                Isolation = TenantIsolation.SharedDatabase,
                Modules = { new TenantModule { ModuleName = "Hr" } }
            });

        await db.SaveChangesAsync();
    }
}
