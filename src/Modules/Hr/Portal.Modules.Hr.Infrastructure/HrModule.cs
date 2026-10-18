using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Portal.Modules.Hr.Application;
using Portal.Modules.Hr.Contracts;
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
        services.AddScoped<IEmployeeService, EmployeeService>();
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

        employees.MapGet("/", async (IEmployeeService service, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(ct)))
            .RequireAuthorization(HrPermissions.Employees.Read);

        employees.MapPost("/", async (EmployeeRequest req, IEmployeeService service, CancellationToken ct) =>
            {
                try
                {
                    var employee = await service.CreateAsync(new EmployeeInput(req.FullName, req.Department), ct);
                    return Results.Created($"/api/hr/employees/{employee.Id}", employee);
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
            })
            .RequireAuthorization(HrPermissions.Employees.Create);

        employees.MapPut("/{id:guid}", async (Guid id, EmployeeRequest req, IEmployeeService service, CancellationToken ct) =>
            {
                try
                {
                    var updated = await service.UpdateAsync(id, new EmployeeInput(req.FullName, req.Department), ct);
                    return updated is null ? Results.NotFound() : Results.Ok(updated);
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
            })
            .RequireAuthorization(HrPermissions.Employees.Update);

        employees.MapDelete("/{id:guid}", async (Guid id, IEmployeeService service, CancellationToken ct) =>
                await service.DeleteAsync(id, ct) ? Results.NoContent() : Results.NotFound())
            .RequireAuthorization(HrPermissions.Employees.Delete);
    }
}
