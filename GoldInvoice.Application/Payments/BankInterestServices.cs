namespace GoldInvoice.Application.Payments;

public sealed record BankDepositInfo(
    Guid Id,
    string BankName,
    string Title,
    string? AccountNumber,
    long PrincipalRials,
    decimal AnnualInterestRatePercent,
    DateOnly OpenedOn,
    DateOnly? MaturityOn,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ClosedAt);

public sealed record BankInterestEntryInfo(
    Guid Id,
    Guid? DepositId,
    string Direction,
    string BankName,
    DateOnly OccurredOn,
    long AmountRials,
    string? Reference,
    DateTimeOffset CreatedAt);

public sealed record BankInterestSnapshotInfo(
    IReadOnlyList<BankDepositInfo> Deposits,
    IReadOnlyList<BankInterestEntryInfo> Entries);

public sealed record CreateBankDepositCommand(
    string BankName,
    string Title,
    string? AccountNumber,
    long PrincipalRials,
    decimal AnnualInterestRatePercent,
    DateOnly OpenedOn,
    DateOnly? MaturityOn);

public sealed record AddBankInterestEntryCommand(
    Guid? DepositId,
    string Direction,
    string BankName,
    DateOnly OccurredOn,
    long AmountRials,
    string? Reference);

public interface IBankInterestService
{
    Task<BankInterestSnapshotInfo> GetSnapshotAsync(
        CancellationToken cancellationToken);

    Task<BankDepositInfo> CreateDepositAsync(
        CreateBankDepositCommand command,
        CancellationToken cancellationToken);

    Task<BankInterestEntryInfo> AddEntryAsync(
        AddBankInterestEntryCommand command,
        CancellationToken cancellationToken);

    Task<BankDepositInfo> CloseDepositAsync(
        Guid depositId,
        CancellationToken cancellationToken);
}
