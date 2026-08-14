using GoldInvoice.Domain.Common;

namespace GoldInvoice.Domain.Payments;

public enum PaymentStatus
{
    Pending,
    Processing,
    Verified,
    RequiresReview,
    Failed,
    Cancelled,
    Refunded
}

public enum PaymentMethod
{
    OnlineGateway,
    Cash,
    PointOfSale,
    BankTransfer,
    CardToCard,
    Installment,
    TrustFund
}

public enum PaymentAttemptStatus
{
    Started,
    Redirected,
    Completed,
    Failed
}

public sealed class PaymentGateway : AuditableEntity, IProtectedFromHardDelete
{
    private PaymentGateway()
    {
    }

    public PaymentGateway(
        string code,
        string displayName,
        string providerCode,
        string configurationReference)
    {
        Code = Guard.Required(code, nameof(code), 50).ToUpperInvariant();
        SetValues(displayName, providerCode, configurationReference, isActive: true);
    }

    public string Code { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    public string ProviderCode { get; private set; } = string.Empty;

    public string ConfigurationReference { get; private set; } = string.Empty;

    public bool IsActive { get; private set; } = true;

    public void Update(
        string displayName,
        string providerCode,
        string configurationReference,
        bool isActive) =>
        SetValues(displayName, providerCode, configurationReference, isActive);

    private void SetValues(
        string displayName,
        string providerCode,
        string configurationReference,
        bool isActive)
    {
        DisplayName = Guard.Required(displayName, nameof(displayName), 200);
        ProviderCode = Guard.Required(providerCode, nameof(providerCode), 100).ToUpperInvariant();
        ConfigurationReference = Guard.Required(
            configurationReference,
            nameof(configurationReference),
            500);
        IsActive = isActive;
    }
}

public sealed class Payment : AuditableEntity, IProtectedFromHardDelete
{
    private Payment()
    {
    }

    public Payment(
        Guid orderId,
        string provider,
        long amountRials,
        PaymentMethod method = PaymentMethod.OnlineGateway,
        Guid? paymentGatewayId = null,
        string? idempotencyKeyHash = null)
    {
        Guard.AgainstEmpty(orderId, nameof(orderId));
        Guard.AgainstNonPositive(amountRials, nameof(amountRials));
        if (paymentGatewayId == Guid.Empty)
        {
            throw new ArgumentException("The payment-gateway identifier cannot be empty.", nameof(paymentGatewayId));
        }

        if (method == PaymentMethod.OnlineGateway && paymentGatewayId is null)
        {
            // Legacy Phase 2 rows and direct domain tests may omit the gateway identifier.
            // Phase 5 services always supply it for a newly initiated online payment.
        }
        else if (method != PaymentMethod.OnlineGateway && paymentGatewayId is not null)
        {
            throw new ArgumentException("Manual payments cannot reference an online gateway.", nameof(paymentGatewayId));
        }

        OrderId = orderId;
        PaymentGatewayId = paymentGatewayId;
        Provider = Guard.Required(provider, nameof(provider), 100).ToUpperInvariant();
        Method = method;
        AmountRials = amountRials;
        IdempotencyKeyHash = Guard.Optional(idempotencyKeyHash, nameof(idempotencyKeyHash), 128)?.ToUpperInvariant();
    }

    public Guid OrderId { get; private set; }

    public Guid? PaymentGatewayId { get; private set; }

    public string Provider { get; private set; } = string.Empty;

    public PaymentMethod Method { get; private set; } = PaymentMethod.OnlineGateway;

    public PaymentStatus Status { get; private set; } = PaymentStatus.Pending;

    public long AmountRials { get; private set; }

    public string? IdempotencyKeyHash { get; private set; }

    public string? Authority { get; private set; }

    public string? GatewayPaymentId { get; private set; }

    public DateTimeOffset? VerifiedAt { get; private set; }

    public DateTimeOffset? FailedAt { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public string? FailureCode { get; private set; }

    public void BeginProcessing(string authority)
    {
        EnsureStatus(PaymentStatus.Pending);
        Authority = Guard.Required(authority, nameof(authority), 200);
        Status = PaymentStatus.Processing;
    }

    public void Verify(string gatewayPaymentId, DateTimeOffset verifiedAt)
    {
        Guard.AgainstDefault(verifiedAt, nameof(verifiedAt));
        if (Status is not PaymentStatus.Pending and not PaymentStatus.Processing)
        {
            throw new DomainConflictException("Only a pending payment can be verified.");
        }

        GatewayPaymentId = Guard.Required(gatewayPaymentId, nameof(gatewayPaymentId), 200);
        Status = PaymentStatus.Verified;
        VerifiedAt = verifiedAt;
        FailedAt = null;
        FailureCode = null;
    }

    public void RequireReview(string failureCode)
    {
        if (Status is PaymentStatus.Verified or PaymentStatus.Cancelled or PaymentStatus.Refunded)
        {
            throw new DomainConflictException("This payment cannot enter review.");
        }

        FailureCode = Guard.Required(failureCode, nameof(failureCode), 100);
        Status = PaymentStatus.RequiresReview;
    }

    public void Fail(string failureCode, DateTimeOffset failedAt)
    {
        Guard.AgainstDefault(failedAt, nameof(failedAt));
        if (Status is PaymentStatus.Verified or PaymentStatus.Cancelled or PaymentStatus.Refunded)
        {
            throw new DomainConflictException("This payment cannot fail from its current state.");
        }

        FailureCode = Guard.Required(failureCode, nameof(failureCode), 100);
        Status = PaymentStatus.Failed;
        FailedAt = failedAt;
    }

    public void Cancel(DateTimeOffset cancelledAt)
    {
        Guard.AgainstDefault(cancelledAt, nameof(cancelledAt));
        if (Status is PaymentStatus.Verified or PaymentStatus.Refunded)
        {
            throw new DomainConflictException("A verified payment cannot be cancelled.");
        }

        Status = PaymentStatus.Cancelled;
        CancelledAt = cancelledAt;
    }

    private void EnsureStatus(PaymentStatus expected)
    {
        if (Status != expected)
        {
            throw new DomainConflictException($"The payment must be {expected}.");
        }
    }
}

public sealed class PaymentAttempt : AuditableEntity, IProtectedFromHardDelete
{
    private PaymentAttempt()
    {
    }

    public PaymentAttempt(Guid paymentId, int attemptNumber, long amountRials, DateTimeOffset startedAt)
    {
        Guard.AgainstEmpty(paymentId, nameof(paymentId));
        Guard.AgainstNonPositive(attemptNumber, nameof(attemptNumber));
        Guard.AgainstNonPositive(amountRials, nameof(amountRials));
        Guard.AgainstDefault(startedAt, nameof(startedAt));
        PaymentId = paymentId;
        AttemptNumber = attemptNumber;
        AmountRials = amountRials;
        StartedAt = startedAt;
    }

    public Guid PaymentId { get; private set; }

    public int AttemptNumber { get; private set; }

    public long AmountRials { get; private set; }

    public PaymentAttemptStatus Status { get; private set; } = PaymentAttemptStatus.Started;

    public string? ProviderRequestId { get; private set; }

    public string? RedirectUrl { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public string? FailureCode { get; private set; }

    public string? MaskedMetadataJson { get; private set; }

    public void MarkRedirected(
        string providerRequestId,
        string redirectUrl,
        string? maskedMetadataJson = null)
    {
        EnsureStatus(PaymentAttemptStatus.Started);
        ProviderRequestId = Guard.Required(providerRequestId, nameof(providerRequestId), 200);
        RedirectUrl = Guard.Required(redirectUrl, nameof(redirectUrl), 2000);
        MaskedMetadataJson = Guard.Optional(maskedMetadataJson, nameof(maskedMetadataJson), 4000);
        Status = PaymentAttemptStatus.Redirected;
    }

    public void Complete(DateTimeOffset completedAt, string? maskedMetadataJson = null)
    {
        Guard.AgainstDefault(completedAt, nameof(completedAt));
        if (Status is not PaymentAttemptStatus.Started and not PaymentAttemptStatus.Redirected)
        {
            throw new DomainConflictException("Only an active payment attempt can complete.");
        }

        MaskedMetadataJson = Guard.Optional(maskedMetadataJson, nameof(maskedMetadataJson), 4000) ??
            MaskedMetadataJson;
        Status = PaymentAttemptStatus.Completed;
        CompletedAt = completedAt;
        FailureCode = null;
    }

    public void Fail(string failureCode, DateTimeOffset completedAt)
    {
        Guard.AgainstDefault(completedAt, nameof(completedAt));
        if (Status is PaymentAttemptStatus.Completed or PaymentAttemptStatus.Failed)
        {
            throw new DomainConflictException("The payment attempt is already final.");
        }

        FailureCode = Guard.Required(failureCode, nameof(failureCode), 100);
        Status = PaymentAttemptStatus.Failed;
        CompletedAt = completedAt;
    }

    private void EnsureStatus(PaymentAttemptStatus expected)
    {
        if (Status != expected)
        {
            throw new DomainConflictException($"The payment attempt must be {expected}.");
        }
    }
}

public sealed class PaymentCallback : AuditableEntity, IAppendOnlyEntity, IProtectedFromHardDelete
{
    private PaymentCallback()
    {
    }

    public PaymentCallback(
        string provider,
        string externalCallbackId,
        string payloadHash,
        DateTimeOffset receivedAt,
        Guid? paymentId = null,
        bool isVerified = false,
        string? processingResult = null,
        string? maskedPayloadJson = null)
    {
        if (paymentId == Guid.Empty)
        {
            throw new ArgumentException("The payment identifier cannot be empty.", nameof(paymentId));
        }

        Provider = Guard.Required(provider, nameof(provider), 100).ToUpperInvariant();
        ExternalCallbackId = Guard.Required(externalCallbackId, nameof(externalCallbackId), 200);
        PayloadHash = Guard.Required(payloadHash, nameof(payloadHash), 128).ToUpperInvariant();
        Guard.AgainstDefault(receivedAt, nameof(receivedAt));
        PaymentId = paymentId;
        IsVerified = isVerified;
        ProcessingResult = Guard.Optional(processingResult, nameof(processingResult), 500);
        MaskedPayloadJson = Guard.Optional(maskedPayloadJson, nameof(maskedPayloadJson), 4000);
        ReceivedAt = receivedAt;
    }

    public Guid? PaymentId { get; private set; }

    public string Provider { get; private set; } = string.Empty;

    public string ExternalCallbackId { get; private set; } = string.Empty;

    public string PayloadHash { get; private set; } = string.Empty;

    public string? MaskedPayloadJson { get; private set; }

    public bool IsVerified { get; private set; }

    public string? ProcessingResult { get; private set; }

    public DateTimeOffset ReceivedAt { get; private set; }
}
