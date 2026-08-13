using System.ComponentModel.DataAnnotations;

namespace GoldInvoice.Contracts.Business;

public sealed class SupplierResponse
{
    public required Guid Id { get; init; }
    public required string Code { get; init; }
    public required string Name { get; init; }
    public string? ContactName { get; init; }
    public string? PhoneNumber { get; init; }
    public string? Email { get; init; }
    public string? NationalId { get; init; }
    public string? AddressLine { get; init; }
    public string? Notes { get; init; }
    public required bool IsActive { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
    public required string RowVersion { get; init; }
}

public class CreateSupplierRequest
{
    [Required, StringLength(64)]
    public string Code { get; init; } = string.Empty;

    [Required, StringLength(200)]
    public string Name { get; init; } = string.Empty;

    [StringLength(200)]
    public string? ContactName { get; init; }

    [StringLength(32)]
    public string? PhoneNumber { get; init; }

    [EmailAddress, StringLength(256)]
    public string? Email { get; init; }

    [StringLength(32)]
    public string? NationalId { get; init; }

    [StringLength(1000)]
    public string? AddressLine { get; init; }

    [StringLength(2000)]
    public string? Notes { get; init; }
}

public sealed class UpdateSupplierRequest : CreateSupplierRequest
{
    public bool IsActive { get; init; } = true;

    [Required]
    public string RowVersion { get; init; } = string.Empty;
}

public sealed class CustomerInteractionResponse
{
    public required Guid Id { get; init; }
    public required Guid CustomerId { get; init; }
    public required string CustomerName { get; init; }
    public required string InteractionType { get; init; }
    public required string Subject { get; init; }
    public string? Notes { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
    public DateTimeOffset? NextFollowUpAt { get; init; }
    public required string Status { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public required string RowVersion { get; init; }
}

public sealed class CreateCustomerInteractionRequest
{
    public Guid CustomerId { get; init; }

    [Required, StringLength(50)]
    public string InteractionType { get; init; } = string.Empty;

    [Required, StringLength(200)]
    public string Subject { get; init; } = string.Empty;

    [StringLength(4000)]
    public string? Notes { get; init; }

    public DateTimeOffset? OccurredAt { get; init; }

    public DateTimeOffset? NextFollowUpAt { get; init; }
}

public sealed class ChangeCustomerInteractionStatusRequest
{
    [Required, StringLength(50)]
    public string Status { get; init; } = string.Empty;

    [Required]
    public string RowVersion { get; init; } = string.Empty;
}
