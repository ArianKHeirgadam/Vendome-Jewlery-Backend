namespace GoldInvoice.Application.Settings;

public sealed record FinancialWorkspaceEntryInfo(
    Guid Id,
    string Scope,
    string EntryType,
    DateOnly OccurredOn,
    long AmountRials,
    string? Reason);

public sealed record CreateFinancialWorkspaceEntryCommand(
    string Scope,
    string EntryType,
    DateOnly OccurredOn,
    long AmountRials,
    string? Reason);

public interface IFinancialWorkspaceService
{
    Task<IReadOnlyList<FinancialWorkspaceEntryInfo>> ListAsync(
        CancellationToken cancellationToken);

    Task<FinancialWorkspaceEntryInfo> CreateAsync(
        CreateFinancialWorkspaceEntryCommand command,
        CancellationToken cancellationToken);
}
