using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Portal.SharedKernel.Tenancy;
using Portal.Tenancy.Data;

namespace Portal.Tenancy;

public interface IModuleAccessService
{
    Task<IReadOnlySet<string>> GetEnabledAsync(Guid tenantId, CancellationToken ct = default);
    Task<bool> IsEnabledAsync(Guid tenantId, string module, CancellationToken ct = default);
}

public sealed class ModuleAccessService(TenancyDb db, IMemoryCache cache) : IModuleAccessService
{
    public async Task<IReadOnlySet<string>> GetEnabledAsync(Guid tenantId, CancellationToken ct = default)
    {
        var set = await cache.GetOrCreateAsync($"modules:{tenantId}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1);
            var now = DateTimeOffset.UtcNow;
            var names = await db.TenantModules.AsNoTracking()
                .Where(m => m.TenantId == tenantId && m.IsEnabled && (m.ExpiresAt == null || m.ExpiresAt > now))
                .Select(m => m.ModuleName)
                .ToListAsync();
            return (IReadOnlySet<string>)new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        });
        return set ?? new HashSet<string>();
    }

    public async Task<bool> IsEnabledAsync(Guid tenantId, string module, CancellationToken ct = default) =>
        (await GetEnabledAsync(tenantId, ct)).Contains(module);
}

public sealed class ModuleAccessMiddleware(RequestDelegate next, HashSet<string> modules)
{
    public async Task InvokeAsync(HttpContext http, ITenantContext tenant, IModuleAccessService access)
    {
        var segments = http.Request.Path.Value?
            .Split('/', StringSplitOptions.RemoveEmptyEntries) ?? [];

        var module = segments.Length > 0 && segments[0].Equals("api", StringComparison.OrdinalIgnoreCase)
            ? segments.ElementAtOrDefault(1)
            : segments.FirstOrDefault();

        if (module is not null && modules.Contains(module) && tenant.IsResolved
            && !await access.IsEnabledAsync(tenant.TenantId, module, http.RequestAborted))
        {
            http.Response.StatusCode = StatusCodes.Status403Forbidden;
            await http.Response.WriteAsJsonAsync(new { error = $"Module '{module}' is not enabled for this tenant." });
            return;
        }

        await next(http);
    }
}

public static class ModuleAccessExtensions
{
    public static IApplicationBuilder UseModuleAccess(this IApplicationBuilder app, IEnumerable<string> moduleNames) =>
        app.UseMiddleware<ModuleAccessMiddleware>(new HashSet<string>(moduleNames, StringComparer.OrdinalIgnoreCase));
}
