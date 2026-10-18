using System.Security.Claims;
using Portal.SharedKernel.Auditing;
using Portal.SharedKernel.Authorization;

namespace Portal.Tenancy;

public sealed class CurrentUserContext : ICurrentUser
{
    private ClaimsPrincipal _principal = new(new ClaimsIdentity());

    public bool IsAuthenticated { get; private set; }
    public string? Id { get; private set; }
    public string? Name { get; private set; }

    public void Set(ClaimsPrincipal principal)
    {
        _principal = principal;
        IsAuthenticated = principal.Identity?.IsAuthenticated == true;
        Id = principal.FindFirst("sub")?.Value;
        Name = principal.Identity?.Name;
    }

    public bool HasPermission(string permission) =>
        IsAuthenticated && PermissionMatcher.IsGranted(_principal, permission);
}
