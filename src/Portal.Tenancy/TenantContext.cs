using Portal.SharedKernel.Tenancy;

namespace Portal.Tenancy;

public sealed class TenantContext : ITenantContext
{
    private TenantInfo? _tenant;

    public bool IsResolved => _tenant is not null;
    public Guid TenantId => _tenant?.Id ?? throw new InvalidOperationException("Tenant is not resolved.");
    public string Identifier => _tenant?.Identifier ?? throw new InvalidOperationException("Tenant is not resolved.");
    public TenantIsolation Isolation => _tenant?.Isolation ?? TenantIsolation.SharedDatabase;
    public string? ConnectionString => _tenant?.ConnectionString;

    public void Set(TenantInfo tenant) => _tenant = tenant;
}
