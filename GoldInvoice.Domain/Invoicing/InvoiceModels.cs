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

public sealed class Invoice : AuditableEntity, IProtectedFromHardDelete
{
    private Invoice()
    {
    }

    public Invoice(
        Guid orderId,
        Guid customerId,
        string invoiceNumber,
        DateTimeOffset issuedAt,
        long subtotalRials,
        long discountRials,
        long shippingRials)
    {
        Guard.AgainstEmpty(orderId, nameof(orderId));
        Guard.AgainstEmpty(customerId, nameof(customerId));
        Guard.AgainstDefault(issuedAt, nameof(issuedAt));
        Guard.AgainstNegative(subtotalRials, nameof(subtotalRials));
        Guard.AgainstNegative(discountRials, nameof(discountRials));
        Guard.AgainstNegative(shippingRials, nameof(shippingRials));
        if (discountRials > subtotalRials)
        {
            throw new ArgumentOutOfRangeException(nameof(discountRials));
        }

        OrderId = orderId;
        CustomerId = customerId;
        InvoiceNumber = Guard.Required(invoiceNumber, nameof(invoiceNumber), 50).ToUpperInvariant();
        IssuedAt = issuedAt;
        SubtotalRials = subtotalRials;
        DiscountRials = discountRials;
        ShippingRials = shippingRials;
        GrandTotalRials = checked(subtotalRials - discountRials + shippingRials);
    }

    public Guid OrderId { get; private set; }

    public Guid CustomerId { get; private set; }

    public string InvoiceNumber { get; private set; } = string.Empty;

    public InvoiceStatus Status { get; private set; } = InvoiceStatus.Issued;

    public DateTimeOffset IssuedAt { get; private set; }

    public long SubtotalRials { get; private set; }

    public long DiscountRials { get; private set; }

    public long ShippingRials { get; private set; }

    public long GrandTotalRials { get; private set; }

    public string? CustomerNameSnapshot { get; private set; }

    public string? CustomerNationalIdSnapshot { get; private set; }

    public DateTimeOffset? VoidedAt { get; private set; }

    public string? VoidReason { get; private set; }
}

public sealed class InvoiceItem : AuditableEntity, IProtectedFromHardDelete
{
    private InvoiceItem()
    {
    }

    public InvoiceItem(
        Guid invoiceId,
        int lineNumber,
        string sku,
        string productName,
        string variantName,
        decimal weightGrams,
        int purity,
        long unitPriceRials,
        int quantity)
    {
        Guard.AgainstEmpty(invoiceId, nameof(invoiceId));
        Guard.AgainstNonPositive(lineNumber, nameof(lineNumber));
        Guard.AgainstNonPositive(weightGrams, nameof(weightGrams));
        Guard.AgainstOutOfRange(purity, 1, 1000, nameof(purity));
        Guard.AgainstNegative(unitPriceRials, nameof(unitPriceRials));
        Guard.AgainstNonPositive(quantity, nameof(quantity));
        InvoiceId = invoiceId;
        LineNumber = lineNumber;
        Sku = Guard.Required(sku, nameof(sku), 64);
        ProductName = Guard.Required(productName, nameof(productName), 200);
        VariantName = Guard.Required(variantName, nameof(variantName), 200);
        WeightGrams = weightGrams;
        Purity = purity;
        UnitPriceRials = unitPriceRials;
        Quantity = quantity;
        LineTotalRials = checked(unitPriceRials * quantity);
    }

    public Guid InvoiceId { get; private set; }

    public int LineNumber { get; private set; }

    public string Sku { get; private set; } = string.Empty;

    public string ProductName { get; private set; } = string.Empty;

    public string VariantName { get; private set; } = string.Empty;

    public decimal WeightGrams { get; private set; }

    public int Purity { get; private set; }

    public long UnitPriceRials { get; private set; }

    public int Quantity { get; private set; }

    public long LineTotalRials { get; private set; }
}

public sealed class InvoicePrintLog : AuditableEntity, IProtectedFromHardDelete
{
    private InvoicePrintLog()
    {
    }

    public InvoicePrintLog(Guid invoiceId, Guid requestedByUserId, int copies, bool isReprint)
    {
        Guard.AgainstEmpty(invoiceId, nameof(invoiceId));
        Guard.AgainstEmpty(requestedByUserId, nameof(requestedByUserId));
        Guard.AgainstNonPositive(copies, nameof(copies));
        InvoiceId = invoiceId;
        RequestedByUserId = requestedByUserId;
        Copies = copies;
        IsReprint = isReprint;
    }

    public Guid InvoiceId { get; private set; }

    public Guid? DesktopDeviceId { get; private set; }

    public Guid RequestedByUserId { get; private set; }

    public InvoicePrintStatus Status { get; private set; } = InvoicePrintStatus.Requested;

    public int Copies { get; private set; }

    public bool IsReprint { get; private set; }

    public string? ReprintReason { get; private set; }

    public string? PrinterName { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public string? FailureCode { get; private set; }
}
