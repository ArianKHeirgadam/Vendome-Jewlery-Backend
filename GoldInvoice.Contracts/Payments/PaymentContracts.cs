using System.ComponentModel.DataAnnotations;

namespace GoldInvoice.Contracts.Payments;

public sealed class PaymentGatewayResponse
{
    public required Guid Id { get; init; }
    public required string Code { get; init; }
    public required string DisplayName { get; init; }
    public required string ProviderCode { get; init; }
    public string? ConfigurationReference { get; init; }
    public required bool IsActive { get; init; }
    public required bool IsProviderRegistered { get; init; }
    public required string RowVersion { get; init; }
}

public sealed class CreatePaymentGatewayRequest
{
    [Required, StringLength(50)]
    public string Code { get; init; } = string.Empty;

    [Required, StringLength(200)]
    public string DisplayName { get; init; } = string.Empty;

    [Required, StringLength(100)]
    public string ProviderCode { get; init; } = string.Empty;

    [Required, StringLength(500)]
    public string ConfigurationReference { get; init; } = string.Empty;
}

public sealed class UpdatePaymentGatewayRequest
{
    [Required, StringLength(200)]
    public string DisplayName { get; init; } = string.Empty;

    [Required, StringLength(100)]
    public string ProviderCode { get; init; } = string.Empty;

    [Required, StringLength(500)]
    public string ConfigurationReference { get; init; } = string.Empty;

    public bool IsActive { get; init; }

    [Required, StringLength(256)]
    public string RowVersion { get; init; } = string.Empty;
}

public sealed class PaymentAttemptResponse
{
    public required Guid Id { get; init; }
    public required int AttemptNumber { get; init; }
    public required string Status { get; init; }
    public string? ProviderRequestId { get; init; }
    public string? RedirectUrl { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public string? FailureCode { get; init; }
}

public sealed class PaymentResponse
{
    public required Guid Id { get; init; }
    public required Guid OrderId { get; init; }
    public Guid? PaymentGatewayId { get; init; }
    public required string Provider { get; init; }
    public required string Method { get; init; }
    public required string Status { get; init; }
    public required long AmountRials { get; init; }
    public string? Authority { get; init; }
    public string? GatewayPaymentId { get; init; }
    public DateTimeOffset? VerifiedAt { get; init; }
    public DateTimeOffset? FailedAt { get; init; }
    public DateTimeOffset? CancelledAt { get; init; }
    public string? FailureCode { get; init; }
    public Guid? InvoiceId { get; init; }
    public required IReadOnlyList<PaymentAttemptResponse> Attempts { get; init; }
    public required string RowVersion { get; init; }
}

public sealed class InitiatePaymentRequest
{
    public Guid OrderId { get; init; }

    [Required, StringLength(50)]
    public string GatewayCode { get; init; } = string.Empty;
}

public sealed class PaymentInitiationResponse
{
    public required PaymentResponse Payment { get; init; }
    public required string RedirectUrl { get; init; }
}

public sealed class RecordManualPaymentRequest
{
    public Guid OrderId { get; init; }

    [Required, StringLength(50)]
    public string Method { get; init; } = string.Empty;

    [StringLength(200)]
    public string? Reference { get; init; }
}

public sealed class VerifyReviewPaymentRequest
{
    [StringLength(200)]
    public string? GatewayPaymentId { get; init; }

    [Required, StringLength(256)]
    public string RowVersion { get; init; } = string.Empty;
}

public sealed class RejectReviewPaymentRequest
{
    [Required, StringLength(1000)]
    public string Reason { get; init; } = string.Empty;

    [Required, StringLength(256)]
    public string RowVersion { get; init; } = string.Empty;
}

public sealed class PaymentCallbackResponse
{
    public required Guid CallbackId { get; init; }
    public required bool IsVerified { get; init; }
    public required bool IsDuplicate { get; init; }
    public required string ProcessingResult { get; init; }
    public Guid? PaymentId { get; init; }
    public Guid? InvoiceId { get; init; }
}
