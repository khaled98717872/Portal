namespace Portal.Tenancy;

public interface ITenantStore
{
    Task<TenantInfo?> FindByIdentifierAsync(string identifier, CancellationToken ct = default);
    Task<TenantInfo?> FindByIdAsync(Guid id, CancellationToken ct = default);
}
