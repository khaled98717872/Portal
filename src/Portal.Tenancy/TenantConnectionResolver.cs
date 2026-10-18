using Microsoft.Extensions.Configuration;
using Portal.SharedKernel.Tenancy;

namespace Portal.Tenancy;

public interface ITenantConnectionResolver
{
    string Resolve();
}

public sealed class TenantConnectionResolver(ITenantContext tenant, IConfiguration config) : ITenantConnectionResolver
{
    public string Resolve()
    {
        if (tenant.IsResolved
            && tenant.Isolation == TenantIsolation.DedicatedDatabase
            && !string.IsNullOrWhiteSpace(tenant.ConnectionString))
            return tenant.ConnectionString;

        return config.GetConnectionString("Shared")
            ?? throw new InvalidOperationException("ConnectionStrings:Shared is missing.");
    }
}
