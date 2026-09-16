using GoldInvoice.Domain.Catalog;
using GoldInvoice.Domain.Common;

namespace GoldInvoice.Domain.Invoicing;

public enum InvoiceStatus
{
    Issued,
    Voided
}

public enum InvoicePrintStatus
{
    Requested,
    Succeeded,
    Failed
}

public sealed class InvoicePrintJob : AuditableEntity, IProtectedFromHardDelete
{
    private InvoicePrintJob() { }

    public InvoicePrintJob(Guid invoiceId, Guid requestedByUserId, Guid desktopDeviceId, int copies, bool isReprint, string? reprintReason = null, string? idempotencyKey = null)
    {
        Guard.AgainstEmpty(invoiceId, nameof(invoiceId)); Guard.AgainstEmpty(requestedByUserId, nameof(requestedByUserId)); Guard.AgainstEmpty(desktopDeviceId, nameof(desktopDeviceId)); Guard.AgainstNonPositive(copies, nameof(copies));
        if (copies > 20) throw new ArgumentOutOfRangeException(nameof(copies));
        if (isReprint && string.IsNullOrWhiteSpace(reprintReason)) throw new ArgumentException("A reprint reason is required.", nameof(reprintReason));
        if (!string.IsNullOrWhiteSpace(idempotencyKey) && idempotencyKey.Length > 128) throw new ArgumentOutOfRangeException(nameof(idempotencyKey));
        InvoiceId = invoiceId; RequestedByUserId = requestedByUserId; DesktopDeviceId = desktopDeviceId; Copies = copies; IsReprint = isReprint; ReprintReason = Guard.Optional(reprintReason, nameof(reprintReason), 1000); IdempotencyKeyHash = string.IsNullOrWhiteSpace(idempotencyKey) ? null : Guard.Required(idempotencyKey, nameof(idempotencyKey), 128).ToUpperInvariant();
    }

    public Guid InvoiceId { get; private set; }
    public Guid RequestedByUserId { get; private set; }
    public Guid DesktopDeviceId { get; private set; }
    public Guid? DevicePrinterId { get; private set; }
    public Guid? PrintProfileId { get; private set; }
    public InvoicePrintStatus Status { get; private set; } = InvoicePrintStatus.Requested;
    public int Copies { get; private set; }
    public bool IsReprint { get; private set; }
    public string? ReprintReason { get; private set; }
    public string? IdempotencyKeyHash { get; private set; }
    public int RetryCount { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string? FailureCode { get; private set; }
    public string? PrintedAtPrinterName { get; private set; }
    public string? PrintedByAgentSignature { get; private set; }
    public Guid? ClaimToken { get; private set; }
    public Guid? ClaimedByDeviceId { get; private set; }
    public DateTimeOffset? ClaimedAt { get; private set; }
    public DateTimeOffset? LeaseExpiresAt { get; private set; }

    public void AssignResources(Guid? devicePrinterId, Guid? printProfileId)
    {
        if (devicePrinterId == Guid.Empty || printProfileId == Guid.Empty) throw new ArgumentException("Resource identifiers cannot be empty.");
        if (Status != InvoicePrintStatus.Requested) throw new DomainConflictException("Only a requested print job can have its resources assigned.");
        DevicePrinterId = devicePrinterId; PrintProfileId = printProfileId;
    }

    public void Claim(Guid deviceId, Guid claimToken, DateTimeOffset claimedAt, DateTimeOffset leaseExpiresAt)
    {
        Guard.AgainstEmpty(deviceId, nameof(deviceId)); Guard.AgainstEmpty(claimToken, nameof(claimToken)); Guard.AgainstDefault(claimedAt, nameof(claimedAt)); Guard.AgainstDefault(leaseExpiresAt, nameof(leaseExpiresAt));
        if (Status != InvoicePrintStatus.Requested) throw new DomainConflictException("Only a requested print job can be claimed.");
        if (leaseExpiresAt <= claimedAt) throw new ArgumentException("Lease expiry must be after claim time.", nameof(leaseExpiresAt));
        if (ClaimedByDeviceId is not null && LeaseExpiresAt > claimedAt && ClaimedByDeviceId != deviceId) throw new DomainConflictException("The print job is already leased to another device.");
        ClaimToken = claimToken; ClaimedByDeviceId = deviceId; ClaimedAt = claimedAt; LeaseExpiresAt = leaseExpiresAt;
    }

    public void Retry(DateTimeOffset retriedAt)
    {
        if (Status != InvoicePrintStatus.Failed) throw new DomainConflictException("Only a failed print job can be retried.");
        RetryCount++; Status = InvoicePrintStatus.Requested; CompletedAt = null; FailureCode = null; PrintedAtPrinterName = null; PrintedByAgentSignature = null; ClaimToken = null; ClaimedByDeviceId = null; ClaimedAt = null; LeaseExpiresAt = null;
    }

    public void MarkSucceeded(DateTimeOffset completedAt, string printerName, string agentSignature)
    {
        Guard.AgainstDefault(completedAt, nameof(completedAt)); if (Status != InvoicePrintStatus.Requested) throw new DomainConflictException("Only a requested print job can succeed.");
        Status = InvoicePrintStatus.Succeeded; CompletedAt = completedAt; PrintedAtPrinterName = Guard.Required(printerName, nameof(printerName), 300); PrintedByAgentSignature = Guard.Required(agentSignature, nameof(agentSignature), 512); FailureCode = null; ClaimToken = null; ClaimedByDeviceId = null; ClaimedAt = null; LeaseExpiresAt = null;
    }

    public void MarkFailed(DateTimeOffset completedAt, string failureCode)
    {
        Guard.AgainstDefault(completedAt, nameof(completedAt)); if (Status != InvoicePrintStatus.Requested) throw new DomainConflictException("Only a requested print job can fail.");
        Status = InvoicePrintStatus.Failed; CompletedAt = completedAt; FailureCode = Guard.Required(failureCode, nameof(failureCode), 100); ClaimToken = null; ClaimedByDeviceId = null; ClaimedAt = null; LeaseExpiresAt = null;
    }
}

public sealed class InvoiceSequence : AuditableEntity, IProtectedFromHardDelete
{
    private InvoiceSequence() { }
    public InvoiceSequence(string series, string prefix, long nextValue = 1) { Guard.AgainstNonPositive(nextValue, nameof(nextValue)); Series = Guard.Required(series, nameof(series), 50).ToUpperInvariant(); Prefix = Guard.Required(prefix, nameof(prefix), 20).ToUpperInvariant(); NextValue = nextValue; }
    public string Series { get; private set; } = string.Empty;
    public string Prefix { get; private set; } = string.Empty;
    public long NextValue { get; private set; }
    public DateTimeOffset? LastIssuedAt { get; private set; }
    public string AllocateNext(DateTimeOffset issuedAt) { Guard.AgainstDefault(issuedAt, nameof(issuedAt)); var value = NextValue; NextValue = checked(NextValue + 1); LastIssuedAt = issuedAt; var invoiceNumber = $"{Prefix}-{value:D10}"; if (invoiceNumber.Length > 50) throw new InvalidOperationException("The invoice sequence produced an overlong number."); return invoiceNumber; }
}

public sealed class Invoice : AuditableEntity, IProtectedFromHardDelete
{
    private Invoice() { }
    public Invoice(Guid orderId, Guid customerId, string invoiceNumber, DateTimeOffset issuedAt, long subtotalRials, long discountRials, long shippingRials, Guid? paymentId = null, string? customerNameSnapshot = null, string? customerNationalIdSnapshot = null)
    {
        Guard.AgainstEmpty(orderId, nameof(orderId)); Guard.AgainstEmpty(customerId, nameof(customerId)); Guard.AgainstDefault(issuedAt, nameof(issuedAt)); Guard.AgainstNegative(subtotalRials, nameof(subtotalRials)); Guard.AgainstNegative(discountRials, nameof(discountRials)); Guard.AgainstNegative(shippingRials, nameof(shippingRials));
        if (paymentId == Guid.Empty) throw new ArgumentException("The payment identifier cannot be empty.", nameof(paymentId)); if (discountRials > subtotalRials) throw new ArgumentOutOfRangeException(nameof(discountRials));
        OrderId = orderId; CustomerId = customerId; PaymentId = paymentId; InvoiceNumber = Guard.Required(invoiceNumber, nameof(invoiceNumber), 50).ToUpperInvariant(); IssuedAt = issuedAt; SubtotalRials = subtotalRials; DiscountRials = discountRials; ShippingRials = shippingRials; GrandTotalRials = checked(subtotalRials - discountRials + shippingRials); CustomerNameSnapshot = Guard.Optional(customerNameSnapshot, nameof(customerNameSnapshot), 200); CustomerNationalIdSnapshot = Guard.Optional(customerNationalIdSnapshot, nameof(customerNationalIdSnapshot), 32);
    }
    public Guid OrderId { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid? PaymentId { get; private set; }
    public string InvoiceNumber { get; private set; } = string.Empty;
    public InvoiceStatus Status { get; private set; } = InvoiceStatus.Issued;
    public DateTimeOffset IssuedAt { get; private set; }
    public long SubtotalRials { get; private set; }
    public long DiscountRials { get; private set; }
    public long ShippingRials { get; private set; }
    public long GrandTotalRials { get; private set; }
    public string? CustomerNameSnapshot { get; private set; }
    public string? CustomerNationalIdSnapshot { get; private set; }
}
