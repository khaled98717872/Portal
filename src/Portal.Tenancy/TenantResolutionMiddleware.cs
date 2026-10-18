using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace Portal.Tenancy;

public sealed class TenantResolutionMiddleware(RequestDelegate next, IHostEnvironment env)
{
    public const string HeaderName = "X-Tenant";
    public const string TenantClaim = "tenant_id";

    public async Task InvokeAsync(HttpContext http, TenantContext context, ITenantStore store, CurrentUserContext currentUser)
    {
        currentUser.Set(http.User);
        TenantInfo? tenant = null;
        var claim = http.User.FindFirst(TenantClaim)?.Value;

        if (http.User.Identity?.IsAuthenticated == true && claim is not null)
        {
            if (!Guid.TryParse(claim, out var id) || (tenant = await store.FindByIdAsync(id, http.RequestAborted)) is null)
            {
                http.Response.StatusCode = StatusCodes.Status403Forbidden;
                await http.Response.WriteAsJsonAsync(new { error = "Invalid tenant claim." });
                return;
            }
        }
        else if (env.IsDevelopment())
        {
            var identifier = ResolveIdentifier(http);
            if (identifier is not null)
            {
                tenant = await store.FindByIdentifierAsync(identifier, http.RequestAborted);
                if (tenant is null)
                {
                    http.Response.StatusCode = StatusCodes.Status404NotFound;
                    await http.Response.WriteAsJsonAsync(new { error = $"Unknown tenant '{identifier}'." });
                    return;
                }
            }
        }

        if (tenant is not null) context.Set(tenant);
        await next(http);
    }

    private static string? ResolveIdentifier(HttpContext http)
    {
        if (http.Request.Headers.TryGetValue(HeaderName, out var header) && !string.IsNullOrWhiteSpace(header))
            return header.ToString();

        var parts = http.Request.Host.Host.Split('.');
        if (parts.Length >= 3 && !parts[0].Equals("www", StringComparison.OrdinalIgnoreCase))
            return parts[0];

        return null;
    }
}

