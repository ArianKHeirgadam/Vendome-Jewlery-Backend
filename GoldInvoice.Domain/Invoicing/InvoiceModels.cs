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

public sealed class InvoiceSequence : AuditableEntity, IProtectedFromHardDelete
{
    private InvoiceSequence()
    {
    }

    public InvoiceSequence(string series, string prefix, long nextValue = 1)
    {
        Guard.AgainstNonPositive(nextValue, nameof(nextValue));
        Series = Guard.Required(series, nameof(series), 50).ToUpperInvariant();
        Prefix = Guard.Required(prefix, nameof(prefix), 20).ToUpperInvariant();
        NextValue = nextValue;
    }

    public string Series { get; private set; } = string.Empty;

    public string Prefix { get; private set; } = string.Empty;

    public long NextValue { get; private set; }

    public DateTimeOffset? LastIssuedAt { get; private set; }

    public string AllocateNext(DateTimeOffset issuedAt)
    {
        Guard.AgainstDefault(issuedAt, nameof(issuedAt));
        var value = NextValue;
        NextValue = checked(NextValue + 1);
        LastIssuedAt = issuedAt;
        var invoiceNumber = $"{Prefix}-{value:D10}";
        if (invoiceNumber.Length > 50)
        {
            throw new InvalidOperationException("The invoice sequence produced an overlong number.");
        }

        return invoiceNumber;
    }
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
        long shippingRials,
        Guid? paymentId = null,
        string? customerNameSnapshot = null,
        string? customerNationalIdSnapshot = null)
    {
        Guard.AgainstEmpty(orderId, nameof(orderId));
        Guard.AgainstEmpty(customerId, nameof(customerId));
        Guard.AgainstDefault(issuedAt, nameof(issuedAt));
        Guard.AgainstNegative(subtotalRials, nameof(subtotalRials));
        Guard.AgainstNegative(discountRials, nameof(discountRials));
        Guard.AgainstNegative(shippingRials, nameof(shippingRials));
        if (paymentId == Guid.Empty)
        {
            throw new ArgumentException("The payment identifier cannot be empty.", nameof(paymentId));
        }

        if (discountRials > subtotalRials)
        {
            throw new ArgumentOutOfRangeException(nameof(discountRials));
        }

        OrderId = orderId;
        CustomerId = customerId;
        PaymentId = paymentId;
        InvoiceNumber = Guard.Required(invoiceNumber, nameof(invoiceNumber), 50).ToUpperInvariant();
        IssuedAt = issuedAt;
        SubtotalRials = subtotalRials;
        DiscountRials = discountRials;
        ShippingRials = shippingRials;
        GrandTotalRials = checked(subtotalRials - discountRials + shippingRials);
        CustomerNameSnapshot = Guard.Optional(customerNameSnapshot, nameof(customerNameSnapshot), 200);
        CustomerNationalIdSnapshot = Guard.Optional(
            customerNationalIdSnapshot,
            nameof(customerNationalIdSnapshot),
            32);
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

    public DateTimeOffset? VoidedAt { get; private set; }

    public string? VoidReason { get; private set; }

    public void Void(DateTimeOffset voidedAt, string reason)
    {
        Guard.AgainstDefault(voidedAt, nameof(voidedAt));
        if (Status != InvoiceStatus.Issued)
        {
            throw new DomainConflictException("Only an issued invoice can be voided.");
        }

        VoidReason = Guard.Required(reason, nameof(reason), 1000);
        Status = InvoiceStatus.Voided;
        VoidedAt = voidedAt;
    }
}

public sealed class InvoiceItem : AuditableEntity, IAppendOnlyEntity, IProtectedFromHardDelete
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
        int quantity,
        Guid? orderItemId = null,
        Guid? priceCalculationSnapshotId = null,
        Guid? inventoryUnitId = null,
        decimal? netGoldWeightGrams = null,
        int? karat = null,
        long? marketUnitPriceRials = null,
        long? goldValueRials = null,
        long? wageRials = null,
        long? profitRials = null,
        long? taxRials = null,
        string? roundingPolicy = null)
    {
        Guard.AgainstEmpty(invoiceId, nameof(invoiceId));
        Guard.AgainstNonPositive(lineNumber, nameof(lineNumber));
        Guard.AgainstNonPositive(weightGrams, nameof(weightGrams));
        Guard.AgainstOutOfRange(purity, 1, 1000, nameof(purity));
        Guard.AgainstNegative(unitPriceRials, nameof(unitPriceRials));
        Guard.AgainstNonPositive(quantity, nameof(quantity));
        if (orderItemId == Guid.Empty ||
            priceCalculationSnapshotId == Guid.Empty ||
            inventoryUnitId == Guid.Empty)
        {
            throw new ArgumentException("Snapshot identifiers cannot be empty.");
        }

        if (orderItemId is not null)
        {
            if (priceCalculationSnapshotId is null ||
                netGoldWeightGrams is not > 0 ||
                netGoldWeightGrams > weightGrams ||
                karat is null ||
                !GoldProductDetail.IsSupportedKarat(karat.Value) ||
                marketUnitPriceRials is < 0 ||
                goldValueRials is < 0 ||
                wageRials is < 0 ||
                profitRials is < 0 ||
                taxRials is < 0 ||
                string.IsNullOrWhiteSpace(roundingPolicy))
            {
                throw new ArgumentException("The invoice item snapshot is incomplete.", nameof(orderItemId));
            }

            if (goldValueRials is null || wageRials is null || profitRials is null || taxRials is null ||
                checked(goldValueRials.Value + wageRials.Value + profitRials.Value + taxRials.Value) != unitPriceRials)
            {
                throw new ArgumentException("The unit price must equal its snapshotted components.", nameof(unitPriceRials));
            }
        }
        else if (priceCalculationSnapshotId is not null ||
                 inventoryUnitId is not null ||
                 netGoldWeightGrams is not null ||
                 karat is not null ||
                 marketUnitPriceRials is not null ||
                 goldValueRials is not null ||
                 wageRials is not null ||
                 profitRials is not null ||
                 taxRials is not null ||
                 roundingPolicy is not null)
        {
            throw new ArgumentException("Legacy invoice items cannot contain a partial price snapshot.");
        }

        InvoiceId = invoiceId;
        OrderItemId = orderItemId;
        PriceCalculationSnapshotId = priceCalculationSnapshotId;
        InventoryUnitId = inventoryUnitId;
        LineNumber = lineNumber;
        Sku = Guard.Required(sku, nameof(sku), 64);
        ProductName = Guard.Required(productName, nameof(productName), 200);
        VariantName = Guard.Required(variantName, nameof(variantName), 200);
        WeightGrams = weightGrams;
        NetGoldWeightGrams = netGoldWeightGrams;
        Purity = purity;
        Karat = karat;
        MarketUnitPriceRials = marketUnitPriceRials;
        GoldValueRials = goldValueRials;
        WageRials = wageRials;
        ProfitRials = profitRials;
        TaxRials = taxRials;
        UnitPriceRials = unitPriceRials;
        Quantity = quantity;
        LineTotalRials = checked(unitPriceRials * quantity);
        RoundingPolicy = Guard.Optional(roundingPolicy, nameof(roundingPolicy), 100);
    }

    public Guid InvoiceId { get; private set; }

    public Guid? OrderItemId { get; private set; }

    public Guid? PriceCalculationSnapshotId { get; private set; }

    public Guid? InventoryUnitId { get; private set; }

    public int LineNumber { get; private set; }

    public string Sku { get; private set; } = string.Empty;

    public string ProductName { get; private set; } = string.Empty;

    public string VariantName { get; private set; } = string.Empty;

    public decimal WeightGrams { get; private set; }

    public decimal? NetGoldWeightGrams { get; private set; }

    public int Purity { get; private set; }

    public int? Karat { get; private set; }

    public long? MarketUnitPriceRials { get; private set; }

    public long? GoldValueRials { get; private set; }

    public long? WageRials { get; private set; }

    public long? ProfitRials { get; private set; }

    public long? TaxRials { get; private set; }

    public long UnitPriceRials { get; private set; }

    public int Quantity { get; private set; }

    public long LineTotalRials { get; private set; }

    public string? RoundingPolicy { get; private set; }
}

public sealed class InvoiceAddressSnapshot : AuditableEntity, IAppendOnlyEntity, IProtectedFromHardDelete
{
    private InvoiceAddressSnapshot()
    {
    }

    public InvoiceAddressSnapshot(
        Guid invoiceId,
        Guid orderAddressSnapshotId,
        string recipientName,
        string phoneNumber,
        string province,
        string city,
        string postalCode,
        string addressLine)
    {
        Guard.AgainstEmpty(invoiceId, nameof(invoiceId));
        Guard.AgainstEmpty(orderAddressSnapshotId, nameof(orderAddressSnapshotId));
        InvoiceId = invoiceId;
        OrderAddressSnapshotId = orderAddressSnapshotId;
        RecipientName = Guard.Required(recipientName, nameof(recipientName), 200);
        PhoneNumber = Guard.Required(phoneNumber, nameof(phoneNumber), 32);
        Province = Guard.Required(province, nameof(province), 100);
        City = Guard.Required(city, nameof(city), 100);
        PostalCode = Guard.Required(postalCode, nameof(postalCode), 20);
        AddressLine = Guard.Required(addressLine, nameof(addressLine), 1000);
    }

    public Guid InvoiceId { get; private set; }

    public Guid OrderAddressSnapshotId { get; private set; }

    public string RecipientName { get; private set; } = string.Empty;

    public string PhoneNumber { get; private set; } = string.Empty;

    public string Province { get; private set; } = string.Empty;

    public string City { get; private set; } = string.Empty;

    public string PostalCode { get; private set; } = string.Empty;

    public string AddressLine { get; private set; } = string.Empty;
}

public sealed class InvoiceStoreSnapshot : AuditableEntity, IAppendOnlyEntity, IProtectedFromHardDelete
{
    private InvoiceStoreSnapshot()
    {
    }

    public InvoiceStoreSnapshot(
        Guid invoiceId,
        Guid orderStoreSnapshotId,
        string tradeName,
        string legalName,
        string? nationalId,
        string? economicCode,
        string? registrationNumber,
        string phoneNumber,
        string postalCode,
        string addressLine)
    {
        Guard.AgainstEmpty(invoiceId, nameof(invoiceId));
        Guard.AgainstEmpty(orderStoreSnapshotId, nameof(orderStoreSnapshotId));
        InvoiceId = invoiceId;
        OrderStoreSnapshotId = orderStoreSnapshotId;
        TradeName = Guard.Required(tradeName, nameof(tradeName), 200);
        LegalName = Guard.Required(legalName, nameof(legalName), 200);
        NationalId = Guard.Optional(nationalId, nameof(nationalId), 32);
        EconomicCode = Guard.Optional(economicCode, nameof(economicCode), 32);
        RegistrationNumber = Guard.Optional(registrationNumber, nameof(registrationNumber), 32);
        PhoneNumber = Guard.Required(phoneNumber, nameof(phoneNumber), 32);
        PostalCode = Guard.Required(postalCode, nameof(postalCode), 20);
        AddressLine = Guard.Required(addressLine, nameof(addressLine), 1000);
    }

    public Guid InvoiceId { get; private set; }

    public Guid OrderStoreSnapshotId { get; private set; }

    public string TradeName { get; private set; } = string.Empty;

    public string LegalName { get; private set; } = string.Empty;

    public string? NationalId { get; private set; }

    public string? EconomicCode { get; private set; }

    public string? RegistrationNumber { get; private set; }

    public string PhoneNumber { get; private set; } = string.Empty;

    public string PostalCode { get; private set; } = string.Empty;

    public string AddressLine { get; private set; } = string.Empty;
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
