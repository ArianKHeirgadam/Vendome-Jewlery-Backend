using System.Text;
using GoldInvoice.Api.Security;
using GoldInvoice.Application.Payments;
using GoldInvoice.Application.Security;
using GoldInvoice.Contracts.Payments;
using GoldInvoice.Domain.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace GoldInvoice.Api.Controllers;

[ApiController]
[Authorize]
[RequestSizeLimit(64 * 1024)]
[Route("api/v1/payments")]
public sealed class PaymentsController(IPaymentService paymentService) : ControllerBase
{
    [HttpGet("gateways")]
    public async Task<ActionResult<IReadOnlyList<PaymentGatewayResponse>>> GetGateways(
        [FromQuery] bool includeInactive,
        CancellationToken cancellationToken)
    {
        var canManage = User.HasPermission(SecurityPermissions.PaymentsManage);
        var gateways = await paymentService.GetGatewaysAsync(
            includeInactive && canManage,
            cancellationToken);
        return Ok(gateways.Select(gateway => MapGateway(gateway, canManage)).ToArray());
    }

    [Authorize(Policy = SecurityPermissions.PaymentsManage)]
    [HttpPost("gateways")]
    public async Task<ActionResult<PaymentGatewayResponse>> CreateGateway(
        CreatePaymentGatewayRequest request,
        CancellationToken cancellationToken)
    {
        var gateway = await paymentService.CreateGatewayAsync(
            new CreatePaymentGatewayCommand(
                request.Code,
                request.DisplayName,
                request.ProviderCode,
                request.ConfigurationReference),
            cancellationToken);
        return Created("/api/v1/payments/gateways", MapGateway(gateway, includeConfiguration: true));
    }

    [Authorize(Policy = SecurityPermissions.PaymentsManage)]
    [HttpPut("gateways/{gatewayId:guid}")]
    public async Task<ActionResult<PaymentGatewayResponse>> UpdateGateway(
        Guid gatewayId,
        UpdatePaymentGatewayRequest request,
        CancellationToken cancellationToken) =>
        Ok(MapGateway(await paymentService.UpdateGatewayAsync(
            gatewayId,
            new UpdatePaymentGatewayCommand(
                request.DisplayName,
                request.ProviderCode,
                request.ConfigurationReference,
                request.IsActive,
                request.RowVersion),
            cancellationToken), includeConfiguration: true));

    [HttpGet("{paymentId:guid}")]
    public async Task<ActionResult<PaymentResponse>> GetPayment(
        Guid paymentId,
        CancellationToken cancellationToken) =>
        Ok(Map(await paymentService.GetPaymentAsync(
            paymentId,
            User.GetRequiredUserId(),
            CanReadAll(),
            cancellationToken)));

    [HttpGet]
    public async Task<ActionResult<GoldInvoice.Contracts.Common.PagedResponse<PaymentResponse>>> GetPayments(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default)
    {
        var result = await paymentService.GetPaymentsAsync(
            User.GetRequiredUserId(),
            CanReadAll(),
            page,
            pageSize,
            ParseOptionalStatus(status),
            cancellationToken);
        return Ok(new GoldInvoice.Contracts.Common.PagedResponse<PaymentResponse>
        {
            Items = result.Items.Select(Map).ToArray(),
            Page = result.Page,
            PageSize = result.PageSize,
            TotalCount = result.TotalCount
        });
    }

    [HttpPost("initiate")]
    public async Task<ActionResult<PaymentInitiationResponse>> Initiate(
        InitiatePaymentRequest request,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var result = await paymentService.InitiateAsync(
            new InitiatePaymentCommand(
                User.GetRequiredUserId(),
                User.HasPermission(SecurityPermissions.PaymentsManage),
                request.OrderId,
                request.GatewayCode,
                idempotencyKey),
            cancellationToken);
        return Ok(new PaymentInitiationResponse
        {
            Payment = Map(result.Payment),
            RedirectUrl = result.RedirectUrl
        });
    }

    [Authorize(Policy = SecurityPermissions.PaymentsManage)]
    [HttpPost("manual")]
    public async Task<ActionResult<PaymentResponse>> RecordManual(
        RecordManualPaymentRequest request,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        CancellationToken cancellationToken) =>
        Ok(Map(await paymentService.RecordManualPaymentAsync(
            new RecordManualPaymentCommand(
                User.GetRequiredUserId(),
                request.OrderId,
                ParseMethod(request.Method),
                request.Reference,
                idempotencyKey),
            cancellationToken)));

    [Authorize(Policy = SecurityPermissions.PaymentsManage)]
    [HttpPost("{paymentId:guid}/review/verify")]
    public async Task<ActionResult<PaymentResponse>> VerifyReview(
        Guid paymentId,
        VerifyReviewPaymentRequest request,
        CancellationToken cancellationToken) =>
        Ok(Map(await paymentService.VerifyReviewPaymentAsync(
            new VerifyReviewPaymentCommand(
                User.GetRequiredUserId(),
                paymentId,
                request.GatewayPaymentId,
                request.RowVersion),
            cancellationToken)));

    [Authorize(Policy = SecurityPermissions.PaymentsManage)]
    [HttpPost("{paymentId:guid}/review/reject")]
    public async Task<ActionResult<PaymentResponse>> RejectReview(
        Guid paymentId,
        RejectReviewPaymentRequest request,
        CancellationToken cancellationToken) =>
        Ok(Map(await paymentService.RejectReviewPaymentAsync(
            new RejectReviewPaymentCommand(
                User.GetRequiredUserId(),
                paymentId,
                request.Reason,
                request.RowVersion),
            cancellationToken)));

    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicyNames.PaymentCallback)]
    [HttpPost("callbacks/{providerCode}")]
    public async Task<ActionResult<PaymentCallbackResponse>> Callback(
        string providerCode,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(
            Request.Body,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 4096,
            leaveOpen: true);
        var payload = await reader.ReadToEndAsync(cancellationToken);
        var headers = Request.Headers.ToDictionary(
            header => header.Key,
            header => header.Value.ToString(),
            StringComparer.OrdinalIgnoreCase);
        var result = await paymentService.ProcessCallbackAsync(
            providerCode,
            payload,
            headers,
            cancellationToken);
        return Ok(new PaymentCallbackResponse
        {
            CallbackId = result.CallbackId,
            IsVerified = result.IsVerified,
            IsDuplicate = result.IsDuplicate,
            ProcessingResult = result.ProcessingResult,
            PaymentId = result.PaymentId,
            InvoiceId = result.InvoiceId
        });
    }

    private bool CanReadAll() =>
        User.HasPermission(SecurityPermissions.PaymentsRead) ||
        User.HasPermission(SecurityPermissions.PaymentsManage);

    private static PaymentStatus? ParseOptionalStatus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Enum.TryParse<PaymentStatus>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new ArgumentException("The payment status is invalid.", nameof(value));
    }

    private static PaymentGatewayResponse MapGateway(
        PaymentGatewayInfo gateway,
        bool includeConfiguration) => new()
    {
        Id = gateway.Id,
        Code = gateway.Code,
        DisplayName = gateway.DisplayName,
        ProviderCode = gateway.ProviderCode,
        ConfigurationReference = includeConfiguration ? gateway.ConfigurationReference : null,
        IsActive = gateway.IsActive,
        IsProviderRegistered = gateway.IsProviderRegistered,
        RowVersion = gateway.RowVersion
    };

    private static PaymentResponse Map(PaymentInfo payment) => new()
    {
        Id = payment.Id,
        OrderId = payment.OrderId,
        PaymentGatewayId = payment.PaymentGatewayId,
        Provider = payment.Provider,
        Method = payment.Method.ToString(),
        Status = payment.Status.ToString(),
        AmountRials = payment.AmountRials,
        Authority = payment.Authority,
        GatewayPaymentId = payment.GatewayPaymentId,
        VerifiedAt = payment.VerifiedAt,
        FailedAt = payment.FailedAt,
        CancelledAt = payment.CancelledAt,
        FailureCode = payment.FailureCode,
        InvoiceId = payment.InvoiceId,
        Attempts = payment.Attempts.Select(attempt => new PaymentAttemptResponse
        {
            Id = attempt.Id,
            AttemptNumber = attempt.AttemptNumber,
            Status = attempt.Status.ToString(),
            ProviderRequestId = attempt.ProviderRequestId,
            RedirectUrl = attempt.RedirectUrl,
            StartedAt = attempt.StartedAt,
            CompletedAt = attempt.CompletedAt,
            FailureCode = attempt.FailureCode
        }).ToArray(),
        RowVersion = payment.RowVersion
    };

    private static PaymentMethod ParseMethod(string value) =>
        Enum.TryParse<PaymentMethod>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new ArgumentException("The payment method is invalid.", nameof(value));
}
