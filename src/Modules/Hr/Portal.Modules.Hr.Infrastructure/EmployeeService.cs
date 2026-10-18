using Microsoft.EntityFrameworkCore;
using Portal.Modules.Hr.Application;
using Portal.Modules.Hr.Contracts;
using Portal.Modules.Hr.Domain;
using Portal.SharedKernel.Auditing;

namespace Portal.Modules.Hr.Infrastructure;

public sealed class EmployeeService(HrDbContext db, ICurrentUser user) : IEmployeeService
{
    public async Task<IReadOnlyList<EmployeeDto>> ListAsync(CancellationToken ct = default)
    {
        Require(HrPermissions.Employees.Read);

        return await db.Employees.AsNoTracking()
            .OrderBy(e => e.FullName)
            .Select(e => new EmployeeDto(e.Id, e.FullName, e.Department))
            .ToListAsync(ct);
    }

    public async Task<EmployeeDto> CreateAsync(EmployeeInput input, CancellationToken ct = default)
    {
        Require(HrPermissions.Employees.Create);
        var (name, department) = Validate(input);

        var employee = new Employee { FullName = name, Department = department };
        db.Employees.Add(employee);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        return new EmployeeDto(employee.Id, employee.FullName, employee.Department);
    }

    public async Task<EmployeeDto?> UpdateAsync(Guid id, EmployeeInput input, CancellationToken ct = default)
    {
        Require(HrPermissions.Employees.Update);
        var (name, department) = Validate(input);

        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (employee is null) return null;

        employee.FullName = name;
        employee.Department = department;
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        return new EmployeeDto(employee.Id, employee.FullName, employee.Department);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        Require(HrPermissions.Employees.Delete);

        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (employee is null) return false;

        db.Employees.Remove(employee);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return true;
    }

    public async Task<IReadOnlyList<EmployeeHistoryItem>> GetHistoryAsync(Guid id, CancellationToken ct = default)
    {
        Require(HrPermissions.Audit.Read);

        var key = id.ToString();
        return await db.AuditEntries.AsNoTracking()
            .Where(a => a.EntityType == nameof(Employee) && a.EntityId == key)
            .OrderByDescending(a => a.Timestamp)
            .Select(a => new EmployeeHistoryItem(a.Timestamp, a.Action, a.UserName, a.Changes))
            .ToListAsync(ct);
    }

    private static (string Name, string? Department) Validate(EmployeeInput input)
    {
        var name = input.FullName?.Trim() ?? "";
        var department = string.IsNullOrWhiteSpace(input.Department) ? null : input.Department.Trim();

        if (name.Length == 0)
            throw new InvalidOperationException("Full name is required.");
        if (name.Length > 200)
            throw new InvalidOperationException("Full name is too long (200 characters at most).");
        if (department is { Length: > 100 })
            throw new InvalidOperationException("Department is too long (100 characters at most).");

        return (name, department);
    }

    private void Require(string permission)
    {
        if (!user.HasPermission(permission))
            throw new UnauthorizedAccessException("You do not have permission to perform this action.");
    }
}
