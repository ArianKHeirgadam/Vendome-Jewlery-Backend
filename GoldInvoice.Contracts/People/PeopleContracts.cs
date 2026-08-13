using System.ComponentModel.DataAnnotations;

namespace GoldInvoice.Contracts.People;

public sealed class PersonResponse
{
    public required Guid Id { get; init; }
    public required string DisplayName { get; init; }
    public string? Email { get; init; }
    public string? PhoneNumber { get; init; }
    public required bool IsActive { get; init; }
    public required bool MfaEnabled { get; init; }
    public required IReadOnlyList<string> Roles { get; init; }
    public required int OrderCount { get; init; }
    public required int InvoiceCount { get; init; }
    public required int AddressCount { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? LastActivityAt { get; init; }
}

public sealed class CreateCustomerRequest
{
    [Required, StringLength(200)]
    public string DisplayName { get; init; } = string.Empty;

    [Required, StringLength(32, MinimumLength = 7)]
    public string PhoneNumber { get; init; } = string.Empty;

    [Required, MinLength(12), MaxLength(128)]
    public string TemporaryPassword { get; init; } = string.Empty;
}

public sealed class CreateEmployeeRequest
{
    [Required, StringLength(200)]
    public string DisplayName { get; init; } = string.Empty;

    [Required, EmailAddress, StringLength(256)]
    public string Email { get; init; } = string.Empty;

    [StringLength(32)]
    public string? PhoneNumber { get; init; }

    [Required, MinLength(12), MaxLength(128)]
    public string TemporaryPassword { get; init; } = string.Empty;
}
