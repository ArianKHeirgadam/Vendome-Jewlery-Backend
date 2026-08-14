namespace GoldInvoice.Application.Payments;

public sealed record InstallmentDraftInfo(
    DateOnly DueOn,
    long AmountRials);

public sealed record InstallmentLineInfo(
    Guid Id,
    int Sequence,
    DateOnly DueOn,
    long AmountRials,
    DateTimeOffset? PaidAt,
    string? Reference);

public sealed record InstallmentPlanInfo(
    Guid Id,
    Guid OrderId,
    Guid CustomerId,
    string OrderNumber,
    string CustomerName,
    long TotalAmountRials,
    DateTimeOffset CreatedAt,
    IReadOnlyList<InstallmentLineInfo> Installments,
    Guid? PaymentId,
    Guid? InvoiceId);

public sealed record CreateInstallmentPlanCommand(
    Guid ActorUserId,
    Guid OrderId,
    IReadOnlyList<InstallmentDraftInfo> Installments);

public sealed record PayInstallmentCommand(
    Guid ActorUserId,
    Guid PlanId,
    Guid InstallmentId,
    string? Reference);

public sealed record TrustFundEntryInfo(
    Guid Id,
    Guid CustomerId,
    Guid? OrderId,
    string EntryType,
    long AmountRials,
    DateTimeOffset OccurredAt,
    string? Reference);

public sealed record TrustFundBalanceInfo(
    Guid CustomerId,
    long BalanceRials);

public sealed record TrustFundSnapshotInfo(
    IReadOnlyList<TrustFundEntryInfo> Entries,
    IReadOnlyList<TrustFundBalanceInfo> Balances);

public sealed record AddTrustFundEntryCommand(
    Guid ActorUserId,
    Guid CustomerId,
    string EntryType,
    long AmountRials,
    DateTimeOffset? OccurredAt,
    string? Reference);

public sealed record AllocateTrustFundCommand(
    Guid ActorUserId,
    Guid OrderId,
    string? Reference);

public sealed record TrustFundAllocationInfo(
    Guid EntryId,
    Guid CustomerId,
    Guid OrderId,
    long AllocatedAmountRials,
    long RemainingBalanceRials,
    Guid PaymentId,
    Guid? InvoiceId);

public interface IFlexiblePaymentService
{
    Task<IReadOnlyList<InstallmentPlanInfo>> GetInstallmentPlansAsync(
        CancellationToken cancellationToken);

    Task<InstallmentPlanInfo> GetInstallmentPlanAsync(
        Guid planId,
        CancellationToken cancellationToken);

    Task<InstallmentPlanInfo> CreateInstallmentPlanAsync(
        CreateInstallmentPlanCommand command,
        CancellationToken cancellationToken);

    Task<InstallmentPlanInfo> PayInstallmentAsync(
        PayInstallmentCommand command,
        CancellationToken cancellationToken);

    Task<TrustFundSnapshotInfo> GetTrustFundSnapshotAsync(
        CancellationToken cancellationToken);

    Task<TrustFundBalanceInfo> GetTrustFundBalanceAsync(
        Guid customerId,
        CancellationToken cancellationToken);

    Task<TrustFundEntryInfo> AddTrustFundEntryAsync(
        AddTrustFundEntryCommand command,
        CancellationToken cancellationToken);

    Task<TrustFundAllocationInfo> AllocateTrustFundAsync(
        AllocateTrustFundCommand command,
        CancellationToken cancellationToken);
}
