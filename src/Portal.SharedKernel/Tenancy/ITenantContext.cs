namespace Portal.SharedKernel.Tenancy;

public enum TenantIsolation
{
    SharedDatabase = 0,
    DedicatedDatabase = 1
}

public interface ITenantContext
{
    bool IsResolved { get; }
    Guid TenantId { get; }
    string Identifier { get; }
    TenantIsolation Isolation { get; }
    string? ConnectionString { get; }
}

public interface ITenantScoped
{
    Guid TenantId { get; set; }
}
