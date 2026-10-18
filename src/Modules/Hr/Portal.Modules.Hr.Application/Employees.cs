namespace Portal.Modules.Hr.Application;

public sealed record EmployeeDto(Guid Id, string FullName, string? Department);

public sealed record EmployeeInput(string FullName, string? Department);

public sealed record EmployeeHistoryItem(DateTimeOffset Timestamp, string Action, string? UserName, string Changes);

public interface IEmployeeService
{
    Task<IReadOnlyList<EmployeeDto>> ListAsync(CancellationToken ct = default);
    Task<EmployeeDto> CreateAsync(EmployeeInput input, CancellationToken ct = default);
    Task<EmployeeDto?> UpdateAsync(Guid id, EmployeeInput input, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<EmployeeHistoryItem>> GetHistoryAsync(Guid id, CancellationToken ct = default);
}
