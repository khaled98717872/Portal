using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Portal.Modules.Hr.Contracts;
using Portal.Modules.Hr.Domain;
using Portal.SharedKernel.Authorization;
using Portal.SharedKernel.Modules;
using Portal.SharedKernel.Tenancy;
using Portal.Tenancy.Data;

namespace Portal.Modules.Hr.Infrastructure;

public sealed record EmployeeRequest(string FullName, string? Department);

public sealed class HrModule : IModule
{
    public string Name => "Hr";

    public IReadOnlyCollection<PermissionDefinition> Permissions { get; } =
    [
        new(HrPermissions.Employees.Read, "View employees"),
        new(HrPermissions.Employees.Create, "Create employees"),
        new(HrPermissions.Employees.Update, "Edit employees"),
        new(HrPermissions.Employees.Delete, "Delete employees"),
        new(HrPermissions.Audit.Read, "View audit log")
    ];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddTenantDbContext<HrDbContext>(HrDbContext.Schema);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/hr").RequireAuthorization();
        group.MapGet("/ping", () => Results.Ok(new { module = "Hr", status = "ok" }));
        group.MapAudit<HrDbContext>(HrPermissions.Audit.Read);

        var employees = group.MapGroup("/employees");
        employees.AddEndpointFilter(async (ctx, next) =>
        {
            var tenant = ctx.HttpContext.RequestServices.GetRequiredService<ITenantContext>();
            return tenant.IsResolved
                ? await next(ctx)
                : Results.BadRequest(new { error = "Tenant is required." });
        });

        employees.MapGet("/", async (HrDbContext db, CancellationToken ct) =>
                Results.Ok(await db.Employees.AsNoTracking().ToListAsync(ct)))
            .RequireAuthorization(HrPermissions.Employees.Read);

        employees.MapPost("/", async (EmployeeRequest req, HrDbContext db, CancellationToken ct) =>
            {
                var employee = new Employee { FullName = req.FullName, Department = req.Department };
                db.Employees.Add(employee);
                await db.SaveChangesAsync(ct);
                return Results.Created($"/api/hr/employees/{employee.Id}", employee);
            })
            .RequireAuthorization(HrPermissions.Employees.Create);

        employees.MapPut("/{id:guid}", async (Guid id, EmployeeRequest req, HrDbContext db, CancellationToken ct) =>
            {
                var employee = await db.Employees.FirstOrDefaultAsync(x => x.Id == id, ct);
                if (employee is null) return Results.NotFound();

                employee.FullName = req.FullName;
                employee.Department = req.Department;
                await db.SaveChangesAsync(ct);
                return Results.Ok(employee);
            })
            .RequireAuthorization(HrPermissions.Employees.Update);

        employees.MapDelete("/{id:guid}", async (Guid id, HrDbContext db, CancellationToken ct) =>
            {
                var employee = await db.Employees.FirstOrDefaultAsync(x => x.Id == id, ct);
                if (employee is null) return Results.NotFound();

                db.Employees.Remove(employee);
                await db.SaveChangesAsync(ct);
                return Results.NoContent();
            })
            .RequireAuthorization(HrPermissions.Employees.Delete);
    }
}

