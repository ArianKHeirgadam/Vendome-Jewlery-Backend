using System.ComponentModel.DataAnnotations;

namespace GoldInvoice.Contracts.Payments;

public sealed class CreateBankDepositRequest
{
    [Required, StringLength(120)]
    public string BankName { get; init; } = string.Empty;

    [Required, StringLength(160)]
    public string Title { get; init; } = string.Empty;

    [StringLength(64)]
    public string? AccountNumber { get; init; }

    [Range(1, long.MaxValue)]
    public long PrincipalRials { get; init; }

    [Range(typeof(decimal), "0", "100")]
    public decimal AnnualInterestRatePercent { get; init; }

    public DateOnly OpenedOn { get; init; }

    public DateOnly? MaturityOn { get; init; }
}

public sealed class AddBankInterestEntryRequest
{
    public Guid? DepositId { get; init; }

    [Required, RegularExpression("^(Received|Paid)$")]
    public string Direction { get; init; } = string.Empty;

    [Required, StringLength(120)]
    public string BankName { get; init; } = string.Empty;

    public DateOnly OccurredOn { get; init; }

    [Range(1, long.MaxValue)]
    public long AmountRials { get; init; }

    [StringLength(200)]
    public string? Reference { get; init; }
}
