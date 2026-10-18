using Portal.SharedKernel.Tenancy;

namespace Portal.Tenancy.Data;

public class TenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Identifier { get; set; } = "";
    public string Name { get; set; } = "";
    public TenantIsolation Isolation { get; set; }
    public string? ConnectionString { get; set; }
    public bool IsActive { get; set; } = true;
    public List<TenantModule> Modules { get; set; } = [];
}

public class TenantModule
{
    public Guid TenantId { get; set; }
    public string ModuleName { get; set; } = "";
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset? ExpiresAt { get; set; }
}
