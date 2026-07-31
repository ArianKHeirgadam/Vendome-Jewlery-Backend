using GoldInvoice.Domain.Common;

namespace GoldInvoice.Domain.Payments;

public enum PaymentStatus
{
    Pending,
    Processing,
    Verified,
    Failed,
    Cancelled,
    Refunded
}

public enum PaymentAttemptStatus
{
    Started,
    Redirected,
    Completed,
    Failed
}

public sealed class Payment : AuditableEntity, IProtectedFromHardDelete
{
    private Payment()
    {
    }

    public Payment(Guid orderId, string provider, long amountRials)
    {
        Guard.AgainstEmpty(orderId, nameof(orderId));
        Guard.AgainstNonPositive(amountRials, nameof(amountRials));
        OrderId = orderId;
        Provider = Guard.Required(provider, nameof(provider), 100);
        AmountRials = amountRials;
    }

    public Guid OrderId { get; private set; }

    public string Provider { get; private set; } = string.Empty;

    public PaymentStatus Status { get; private set; } = PaymentStatus.Pending;

    public long AmountRials { get; private set; }

    public string? Authority { get; private set; }

    public string? GatewayPaymentId { get; private set; }

    public DateTimeOffset? VerifiedAt { get; private set; }

    public DateTimeOffset? FailedAt { get; private set; }

    public string? FailureCode { get; private set; }
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

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public string? FailureCode { get; private set; }

    public string? MaskedMetadataJson { get; private set; }
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
        DateTimeOffset receivedAt)
    {
        Provider = Guard.Required(provider, nameof(provider), 100);
        ExternalCallbackId = Guard.Required(externalCallbackId, nameof(externalCallbackId), 200);
        PayloadHash = Guard.Required(payloadHash, nameof(payloadHash), 128);
        Guard.AgainstDefault(receivedAt, nameof(receivedAt));
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
