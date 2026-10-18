using System.Security.Claims;
using Portal.SharedKernel.Auditing;

namespace Portal.Tenancy;

public sealed class CurrentUserContext : ICurrentUser
{
    public bool IsAuthenticated { get; private set; }
    public string? Id { get; private set; }
    public string? Name { get; private set; }

    public void Set(ClaimsPrincipal principal)
    {
        IsAuthenticated = principal.Identity?.IsAuthenticated == true;
        Id = principal.FindFirst("sub")?.Value;
        Name = principal.Identity?.Name;
    }
}
