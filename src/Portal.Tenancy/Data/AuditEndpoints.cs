using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Portal.Tenancy.Data;

public static class AuditEndpoints
{
    public static RouteGroupBuilder MapAudit<TContext>(this RouteGroupBuilder group, string permission)
        where TContext : TenantDbContext
    {
        group.MapGet("/audit", async (TContext db, string? entityType, string? entityId, int? take, CancellationToken ct) =>
        {
            var query = db.AuditEntries.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(entityType)) query = query.Where(a => a.EntityType == entityType);
            if (!string.IsNullOrWhiteSpace(entityId)) query = query.Where(a => a.EntityId == entityId);

            var rows = await query
                .OrderByDescending(a => a.Timestamp)
                .Take(Math.Clamp(take ?? 50, 1, 200))
                .Select(a => new { a.Timestamp, a.Action, a.EntityType, a.EntityId, a.UserName, a.TraceId, a.Changes })
                .ToListAsync(ct);

            return Results.Ok(rows.Select(r => new
            {
                r.Timestamp, r.Action, r.EntityType, r.EntityId, r.UserName, r.TraceId,
                Changes = JsonSerializer.Deserialize<JsonElement>(r.Changes)
            }));
        }).RequireAuthorization(permission);

        return group;
    }
}
