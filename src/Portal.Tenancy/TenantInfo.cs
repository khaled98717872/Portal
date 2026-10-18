using Portal.SharedKernel.Tenancy;

namespace Portal.Tenancy;

public sealed record TenantInfo(
    Guid Id,
    string Identifier,
    string Name,
    TenantIsolation Isolation,
    string? ConnectionString = null);
