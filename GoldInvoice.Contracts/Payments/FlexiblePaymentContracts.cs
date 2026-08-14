using System.ComponentModel.DataAnnotations;

namespace GoldInvoice.Contracts.Payments;

public sealed class InstallmentDraftRequest
{
    public DateOnly DueOn { get; init; }

    [Range(1, long.MaxValue)]
    public long AmountRials { get; init; }
}

public sealed class CreateInstallmentPlanRequest
{
    public Guid OrderId { get; init; }

    [Required, MinLength(1), MaxLength(24)]
    public IReadOnlyList<InstallmentDraftRequest> Installments { get; init; } = [];
}

public sealed class PayInstallmentRequest
{
    [StringLength(200)]
    public string? Reference { get; init; }
}

public sealed class AddTrustFundEntryRequest
{
    public Guid CustomerId { get; init; }

    [Required, RegularExpression("^(Deposit|Release)$")]
    public string EntryType { get; init; } = string.Empty;

    [Range(1, long.MaxValue)]
    public long AmountRials { get; init; }

    public DateTimeOffset? OccurredAt { get; init; }

    [StringLength(200)]
    public string? Reference { get; init; }
}

public sealed class AllocateTrustFundRequest
{
    public Guid OrderId { get; init; }

    [StringLength(200)]
    public string? Reference { get; init; }
}
