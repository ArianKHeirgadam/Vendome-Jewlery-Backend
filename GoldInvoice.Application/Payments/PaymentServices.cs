using GoldInvoice.Domain.Payments;
using GoldInvoice.Application.Common;

namespace GoldInvoice.Application.Payments;

public sealed record PaymentGatewayInfo(
    Guid Id,
    string Code,
    string DisplayName,
    string ProviderCode,
    string ConfigurationReference,
    bool IsActive,
    bool IsProviderRegistered,
    string RowVersion);

public sealed record CreatePaymentGatewayCommand(
    string Code,
    string DisplayName,
    string ProviderCode,
    string ConfigurationReference);

public sealed record UpdatePaymentGatewayCommand(
    string DisplayName,
    string ProviderCode,
    string ConfigurationReference,
    bool IsActive,
    string RowVersion);

public sealed record PaymentAttemptInfo(
    Guid Id,
    int AttemptNumber,
    PaymentAttemptStatus Status,
    string? ProviderRequestId,
    string? RedirectUrl,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string? FailureCode);

public sealed record PaymentInfo(
    Guid Id,
    Guid OrderId,
    Guid? PaymentGatewayId,
    string Provider,
    PaymentMethod Method,
    PaymentStatus Status,
    long AmountRials,
    string? Authority,
    string? GatewayPaymentId,
    DateTimeOffset? VerifiedAt,
    DateTimeOffset? FailedAt,
    DateTimeOffset? CancelledAt,
    string? FailureCode,
    Guid? InvoiceId,
    IReadOnlyList<PaymentAttemptInfo> Attempts,
    string RowVersion);

public sealed record InitiatePaymentCommand(
    Guid ActorUserId,
    bool CanManagePayments,
    Guid OrderId,
    string GatewayCode,
    string IdempotencyKey);

public sealed record RecordManualPaymentCommand(
    Guid ActorUserId,
    Guid OrderId,
    PaymentMethod Method,
    string? Reference,
    string IdempotencyKey);

public sealed record VerifyReviewPaymentCommand(
    Guid ActorUserId,
    Guid PaymentId,
    string? GatewayPaymentId,
    string RowVersion);

public sealed record RejectReviewPaymentCommand(
    Guid ActorUserId,
    Guid PaymentId,
    string Reason,
    string RowVersion);

public sealed record PaymentInitiationInfo(PaymentInfo Payment, string RedirectUrl);

public sealed record PaymentGatewayInitiationRequest(
    Guid PaymentId,
    Guid OrderId,
    string OrderNumber,
    long AmountRials,
    string CustomerName,
    string ConfigurationReference);

public sealed record PaymentGatewayInitiationResult(
    string Authority,
    string ProviderRequestId,
    string RedirectUrl,
    string? MaskedMetadataJson);

public sealed record PaymentGatewayCallbackRequest(
    string RawPayload,
    IReadOnlyDictionary<string, string> Headers,
    string ConfigurationReference);

public sealed record PaymentGatewayCallbackResult(
    bool IsAuthentic,
    string? ExternalCallbackId,
    Guid? MerchantPaymentId,
    string? Authority,
    string? GatewayPaymentId,
    long? AmountRials,
    bool IsSuccessful,
    string? FailureCode,
    string? MaskedPayloadJson);

public sealed record PaymentCallbackProcessingInfo(
    Guid CallbackId,
    bool IsVerified,
    bool IsDuplicate,
    string ProcessingResult,
    Guid? PaymentId,
    Guid? InvoiceId);

public interface IPaymentGatewayProvider
{
    string ProviderCode { get; }

    Task<PaymentGatewayInitiationResult> InitiateAsync(
        PaymentGatewayInitiationRequest request,
        CancellationToken cancellationToken);

    Task<PaymentGatewayCallbackResult> VerifyCallbackAsync(
        PaymentGatewayCallbackRequest request,
        CancellationToken cancellationToken);
}

public interface IPaymentService
{
    Task<IReadOnlyList<PaymentGatewayInfo>> GetGatewaysAsync(
        bool includeInactive,
        CancellationToken cancellationToken);

    Task<PaymentGatewayInfo> CreateGatewayAsync(
        CreatePaymentGatewayCommand command,
        CancellationToken cancellationToken);

    Task<PaymentGatewayInfo> UpdateGatewayAsync(
        Guid gatewayId,
        UpdatePaymentGatewayCommand command,
        CancellationToken cancellationToken);

    Task<PaymentInfo> GetPaymentAsync(
        Guid paymentId,
        Guid actorUserId,
        bool canReadAll,
        CancellationToken cancellationToken);

    Task<PagedResult<PaymentInfo>> GetPaymentsAsync(
        Guid actorUserId,
        bool canReadAll,
        int page,
        int pageSize,
        PaymentStatus? status,
        CancellationToken cancellationToken);

    Task<PaymentInitiationInfo> InitiateAsync(
        InitiatePaymentCommand command,
        CancellationToken cancellationToken);

    Task<PaymentInfo> RecordManualPaymentAsync(
        RecordManualPaymentCommand command,
        CancellationToken cancellationToken);

    Task<PaymentInfo> VerifyReviewPaymentAsync(
        VerifyReviewPaymentCommand command,
        CancellationToken cancellationToken);

    Task<PaymentInfo> RejectReviewPaymentAsync(
        RejectReviewPaymentCommand command,
        CancellationToken cancellationToken);

    Task<PaymentCallbackProcessingInfo> ProcessCallbackAsync(
        string providerCode,
        string rawPayload,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken cancellationToken);
}
