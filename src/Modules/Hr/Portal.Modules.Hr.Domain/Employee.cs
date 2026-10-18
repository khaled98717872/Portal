using Portal.SharedKernel.Tenancy;

namespace Portal.Modules.Hr.Domain;

public class Employee : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string FullName { get; set; } = "";
    public string? Department { get; set; }
}
