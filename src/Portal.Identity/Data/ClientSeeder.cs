using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Portal.Identity.Data;

public static class ClientSeeder
{
    public static async Task SeedAsync(IServiceProvider sp, IConfiguration config)
    {
        var apps = sp.GetRequiredService<IOpenIddictApplicationManager>();

        var hostUrl = (config["Clients:PortalHost:Url"] ?? "http://localhost:5103").TrimEnd('/');
        var hostSecret = config["Clients:PortalHost:Secret"]
            ?? throw new InvalidOperationException("Clients:PortalHost:Secret is missing (use user-secrets).");
        var adminSecret = config["Clients:PortalHostAdmin:Secret"]
            ?? throw new InvalidOperationException("Clients:PortalHostAdmin:Secret is missing (use user-secrets).");

        await UpsertAsync(apps, new OpenIddictApplicationDescriptor
        {
            ClientId = "portal-host",
            ClientSecret = hostSecret,
            ClientType = ClientTypes.Confidential,
            ConsentType = ConsentTypes.Implicit,
            DisplayName = "Portal Host",
            RedirectUris = { new Uri($"{hostUrl}/signin-oidc") },
            PostLogoutRedirectUris = { new Uri($"{hostUrl}/signout-callback-oidc") },
            Permissions =
            {
                Permissions.Endpoints.Authorization,
                Permissions.Endpoints.Token,
                Permissions.Endpoints.EndSession,
                Permissions.GrantTypes.AuthorizationCode,
                Permissions.GrantTypes.RefreshToken,
                Permissions.ResponseTypes.Code,
                Permissions.Scopes.Email,
                Permissions.Scopes.Profile,
                Permissions.Scopes.Roles,
                Permissions.Prefixes.Scope + Scopes.OfflineAccess
            },
            Requirements = { Requirements.Features.ProofKeyForCodeExchange }
        });

        await UpsertAsync(apps, new OpenIddictApplicationDescriptor
        {
            ClientId = "portal-host-admin",
            ClientSecret = adminSecret,
            ClientType = ClientTypes.Confidential,
            DisplayName = "Portal Host (identity admin API)",
            Permissions =
            {
                Permissions.Endpoints.Token,
                Permissions.GrantTypes.ClientCredentials,
                Permissions.Prefixes.Scope + AdminApi.Scope
            }
        });
    }

    private static async Task UpsertAsync(IOpenIddictApplicationManager apps, OpenIddictApplicationDescriptor descriptor)
    {
        var existing = await apps.FindByClientIdAsync(descriptor.ClientId!);
        if (existing is null)
            await apps.CreateAsync(descriptor);
        else
            await apps.UpdateAsync(existing, descriptor);
    }
}
