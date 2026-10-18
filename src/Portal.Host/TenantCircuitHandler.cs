using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Portal.Tenancy;

namespace Portal.Host;

public sealed class TenantCircuitHandler(
    AuthenticationStateProvider auth,
    TenantContext tenantContext,
    CurrentUserContext currentUser,
    ITenantStore store) : CircuitHandler
{
    public override async Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        var state = await auth.GetAuthenticationStateAsync();
        currentUser.Set(state.User);

        var claim = state.User.FindFirst("tenant_id")?.Value;
        if (state.User.Identity?.IsAuthenticated == true
            && Guid.TryParse(claim, out var id)
            && await store.FindByIdAsync(id, cancellationToken) is { } tenant)
        {
            tenantContext.Set(tenant);
        }
    }
}
