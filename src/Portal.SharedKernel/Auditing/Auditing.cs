using Portal.SharedKernel.Tenancy;

namespace Portal.SharedKernel.Auditing;

public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    string? Id { get; }
    string? Name { get; }
    bool HasPermission(string permission);
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class AuditIgnoreAttribute : Attribute { }

public static class AuditActions
{
    public const string Create = "Create";
    public const string Update = "Update";
    public const string Delete = "Delete";
}

public class AuditEntry : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public string Action { get; set; } = "";
    public string EntityType { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string? UserId { get; set; }
    public string? UserName { get; set; }
    public string? TraceId { get; set; }
    public string Changes { get; set; } = "{}";
}

