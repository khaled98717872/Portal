using Microsoft.AspNetCore.Identity;

namespace Portal.Identity.Data;

public class AppUser : IdentityUser<Guid>
{
    public Guid TenantId { get; set; }
    public string? FullName { get; set; }
}
