using System.ComponentModel.DataAnnotations;

namespace GoldInvoice.Contracts.Settings;

public sealed class FinancialWorkspaceResponse
{
    public required IReadOnlyList<FinancialWorkspaceEntryResponse> Entries { get; init; }
}

public sealed class FinancialWorkspaceEntryResponse
{
    public required Guid Id { get; init; }
    public required string Scope { get; init; }
    public required string EntryType { get; init; }
    public required DateOnly OccurredOn { get; init; }
    public required long AmountRials { get; init; }
    public string? Reason { get; init; }
}

public sealed class CreateFinancialWorkspaceEntryRequest
{
    [Required, StringLength(20)]
    [RegularExpression("^(Warehouse|Houman|Ali)$")]
    public string Scope { get; init; } = string.Empty;

    [Required, StringLength(20)]
    [RegularExpression("^(Expense|Asset)$")]
    public string EntryType { get; init; } = string.Empty;

    public DateOnly OccurredOn { get; init; }

    [Range(1, long.MaxValue)]
    public long AmountRials { get; init; }

    [StringLength(500)]
    public string? Reason { get; init; }
}
