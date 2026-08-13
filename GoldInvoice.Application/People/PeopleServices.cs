namespace GoldInvoice.Application.People;

public sealed record PersonInfo(
    Guid Id,
    string DisplayName,
    string? Email,
    string? PhoneNumber,
    bool IsActive,
    bool MfaEnabled,
    IReadOnlyList<string> Roles,
    int OrderCount,
    int InvoiceCount,
    int AddressCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastActivityAt);

public sealed record CreateCustomerCommand(
    string DisplayName,
    string PhoneNumber,
    string TemporaryPassword);

public sealed record CreateEmployeeCommand(
    string DisplayName,
    string Email,
    string? PhoneNumber,
    string TemporaryPassword);

public interface IPeopleDirectoryService
{
    Task<IReadOnlyList<PersonInfo>> GetCustomersAsync(
        string? query,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PersonInfo>> GetEmployeesAsync(
        string? query,
        CancellationToken cancellationToken);

    Task<PersonInfo> CreateCustomerAsync(
        CreateCustomerCommand command,
        CancellationToken cancellationToken);

    Task<PersonInfo> CreateEmployeeAsync(
        CreateEmployeeCommand command,
        CancellationToken cancellationToken);
}
