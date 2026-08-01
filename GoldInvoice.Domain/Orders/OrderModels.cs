using GoldInvoice.Domain.Catalog;
using GoldInvoice.Domain.Common;

namespace GoldInvoice.Domain.Orders;

public enum OrderStatus
{
    Pending,
    AwaitingPayment,
    PaymentReview,
    Paid,
    Processing,
    Completed,
    Cancelled,
    Refunded
}

public sealed class Order : AuditableEntity, IProtectedFromHardDelete
{
    private Order()
    {
    }

    public Order(
        Guid customerId,
        string orderNumber,
        long itemsSubtotalRials,
        long discountRials,
        long shippingRials,
        string? customerNameSnapshot = null,
        string? customerNationalIdSnapshot = null)
    {
        Guard.AgainstEmpty(customerId, nameof(customerId));
        Guard.AgainstNegative(itemsSubtotalRials, nameof(itemsSubtotalRials));
        Guard.AgainstNegative(discountRials, nameof(discountRials));
        Guard.AgainstNegative(shippingRials, nameof(shippingRials));
        if (discountRials > itemsSubtotalRials)
        {
            throw new ArgumentOutOfRangeException(nameof(discountRials), "The discount cannot exceed the item subtotal.");
        }

        CustomerId = customerId;
        OrderNumber = Guard.Required(orderNumber, nameof(orderNumber), 50).ToUpperInvariant();
        ItemsSubtotalRials = itemsSubtotalRials;
        DiscountRials = discountRials;
        ShippingRials = shippingRials;
        GrandTotalRials = checked(itemsSubtotalRials - discountRials + shippingRials);
        if (GrandTotalRials <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(discountRials),
                "A payable order must have a positive grand total.");
        }

        CustomerNameSnapshot = Guard.Optional(customerNameSnapshot, nameof(customerNameSnapshot), 200);
        CustomerNationalIdSnapshot = Guard.Optional(
            customerNationalIdSnapshot,
            nameof(customerNationalIdSnapshot),
            32);
    }

    public Guid CustomerId { get; private set; }

    public string OrderNumber { get; private set; } = string.Empty;

    public OrderStatus Status { get; private set; } = OrderStatus.Pending;

    public long ItemsSubtotalRials { get; private set; }

    public long DiscountRials { get; private set; }

    public long ShippingRials { get; private set; }

    public long GrandTotalRials { get; private set; }

    public string? CustomerNameSnapshot { get; private set; }

    public string? CustomerNationalIdSnapshot { get; private set; }

    public DateTimeOffset? PaidAt { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public void MoveToAwaitingPayment()
    {
        EnsureStatus(OrderStatus.Pending);
        Status = OrderStatus.AwaitingPayment;
    }

    public void MarkPaymentReview()
    {
        if (Status is not OrderStatus.AwaitingPayment and not OrderStatus.PaymentReview)
        {
            throw new DomainConflictException("Only an order awaiting payment can enter payment review.");
        }

        Status = OrderStatus.PaymentReview;
    }

    public void MarkPaid(DateTimeOffset paidAt)
    {
        Guard.AgainstDefault(paidAt, nameof(paidAt));
        if (Status is not OrderStatus.AwaitingPayment and not OrderStatus.PaymentReview)
        {
            throw new DomainConflictException("Only an order awaiting payment can be paid.");
        }

        Status = OrderStatus.Paid;
        PaidAt = paidAt;
    }

    public void MoveToProcessing()
    {
        EnsureStatus(OrderStatus.Paid);
        Status = OrderStatus.Processing;
    }

    public void Complete()
    {
        if (Status is not OrderStatus.Paid and not OrderStatus.Processing)
        {
            throw new DomainConflictException("Only a paid order can be completed.");
        }

        Status = OrderStatus.Completed;
    }

    public void Cancel(DateTimeOffset cancelledAt)
    {
        Guard.AgainstDefault(cancelledAt, nameof(cancelledAt));
        if (Status is not OrderStatus.Pending and
            not OrderStatus.AwaitingPayment and
            not OrderStatus.PaymentReview)
        {
            throw new DomainConflictException("Only an unpaid order can be cancelled.");
        }

        Status = OrderStatus.Cancelled;
        CancelledAt = cancelledAt;
    }

    private void EnsureStatus(OrderStatus expected)
    {
        if (Status != expected)
        {
            throw new DomainConflictException($"The order must be {expected}.");
        }
    }
}

public sealed class OrderItem : AuditableEntity, IAppendOnlyEntity, IProtectedFromHardDelete
{
    private OrderItem()
    {
    }

    public OrderItem(
        Guid orderId,
        Guid productVariantId,
        int lineNumber,
        string sku,
        string productName,
        string variantName,
        decimal weightGrams,
        int purity,
        long unitPriceRials,
        int quantity,
        Guid? priceCalculationSnapshotId = null,
        Guid? inventoryItemId = null,
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
        Guard.AgainstEmpty(orderId, nameof(orderId));
        Guard.AgainstEmpty(productVariantId, nameof(productVariantId));
        Guard.AgainstNonPositive(lineNumber, nameof(lineNumber));
        Guard.AgainstNonPositive(weightGrams, nameof(weightGrams));
        Guard.AgainstOutOfRange(purity, 1, 1000, nameof(purity));
        Guard.AgainstNegative(unitPriceRials, nameof(unitPriceRials));
        Guard.AgainstNonPositive(quantity, nameof(quantity));
        ValidatePhaseFiveSnapshot(
            priceCalculationSnapshotId,
            inventoryItemId,
            inventoryUnitId,
            weightGrams,
            netGoldWeightGrams,
            karat,
            marketUnitPriceRials,
            goldValueRials,
            wageRials,
            profitRials,
            taxRials,
            unitPriceRials,
            roundingPolicy);

        OrderId = orderId;
        ProductVariantId = productVariantId;
        LineNumber = lineNumber;
        Sku = Guard.Required(sku, nameof(sku), 64);
        ProductName = Guard.Required(productName, nameof(productName), 200);
        VariantName = Guard.Required(variantName, nameof(variantName), 200);
        WeightGrams = weightGrams;
        Purity = purity;
        UnitPriceRials = unitPriceRials;
        Quantity = quantity;
        LineTotalRials = checked(unitPriceRials * quantity);
        PriceCalculationSnapshotId = priceCalculationSnapshotId;
        InventoryItemId = inventoryItemId;
        InventoryUnitId = inventoryUnitId;
        NetGoldWeightGrams = netGoldWeightGrams;
        Karat = karat;
        MarketUnitPriceRials = marketUnitPriceRials;
        GoldValueRials = goldValueRials;
        WageRials = wageRials;
        ProfitRials = profitRials;
        TaxRials = taxRials;
        RoundingPolicy = Guard.Optional(roundingPolicy, nameof(roundingPolicy), 100);
    }

    public Guid OrderId { get; private set; }

    public Guid ProductVariantId { get; private set; }

    public Guid? PriceCalculationSnapshotId { get; private set; }

    public Guid? InventoryItemId { get; private set; }

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

    private static void ValidatePhaseFiveSnapshot(
        Guid? priceCalculationSnapshotId,
        Guid? inventoryItemId,
        Guid? inventoryUnitId,
        decimal grossWeightGrams,
        decimal? netGoldWeightGrams,
        int? karat,
        long? marketUnitPriceRials,
        long? goldValueRials,
        long? wageRials,
        long? profitRials,
        long? taxRials,
        long unitPriceRials,
        string? roundingPolicy)
    {
        if (priceCalculationSnapshotId is null)
        {
            if (inventoryItemId is not null ||
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
                throw new ArgumentException(
                    "Legacy order items cannot contain a partial price snapshot.",
                    nameof(priceCalculationSnapshotId));
            }

            return;
        }

        Guard.AgainstEmpty(priceCalculationSnapshotId.Value, nameof(priceCalculationSnapshotId));
        if (inventoryItemId is null || inventoryItemId == Guid.Empty)
        {
            throw new ArgumentException("A priced order item requires an inventory item.", nameof(inventoryItemId));
        }

        if (inventoryUnitId == Guid.Empty)
        {
            throw new ArgumentException("The inventory-unit identifier cannot be empty.", nameof(inventoryUnitId));
        }

        if (netGoldWeightGrams is not > 0 || netGoldWeightGrams > grossWeightGrams)
        {
            throw new ArgumentException("A valid net gold weight is required.", nameof(netGoldWeightGrams));
        }

        if (karat is null || !GoldProductDetail.IsSupportedKarat(karat.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(karat));
        }

        if (marketUnitPriceRials is < 0 ||
            goldValueRials is < 0 ||
            wageRials is < 0 ||
            profitRials is < 0 ||
            taxRials is < 0 ||
            string.IsNullOrWhiteSpace(roundingPolicy))
        {
            throw new ArgumentException("The price component snapshot is incomplete.", nameof(priceCalculationSnapshotId));
        }

        if (goldValueRials is null || wageRials is null || profitRials is null || taxRials is null ||
            checked(goldValueRials.Value + wageRials.Value + profitRials.Value + taxRials.Value) != unitPriceRials)
        {
            throw new ArgumentException("The unit price must equal its snapshotted components.", nameof(unitPriceRials));
        }
    }
}

public sealed class OrderStatusHistory : AuditableEntity, IAppendOnlyEntity, IProtectedFromHardDelete
{
    private OrderStatusHistory()
    {
    }

    public OrderStatusHistory(
        Guid orderId,
        OrderStatus? fromStatus,
        OrderStatus toStatus,
        DateTimeOffset occurredAt,
        Guid? changedBy = null,
        string? reason = null)
    {
        Guard.AgainstEmpty(orderId, nameof(orderId));
        Guard.AgainstDefault(occurredAt, nameof(occurredAt));
        if (changedBy == Guid.Empty)
        {
            throw new ArgumentException("The changing user identifier cannot be empty.", nameof(changedBy));
        }

        OrderId = orderId;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        ChangedBy = changedBy;
        Reason = Guard.Optional(reason, nameof(reason), 1000);
        OccurredAt = occurredAt;
    }

    public Guid OrderId { get; private set; }

    public OrderStatus? FromStatus { get; private set; }

    public OrderStatus ToStatus { get; private set; }

    public Guid? ChangedBy { get; private set; }

    public string? Reason { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }
}

public sealed class OrderAddressSnapshot : AuditableEntity, IAppendOnlyEntity, IProtectedFromHardDelete
{
    private OrderAddressSnapshot()
    {
    }

    public OrderAddressSnapshot(
        Guid orderId,
        string recipientName,
        string phoneNumber,
        string province,
        string city,
        string postalCode,
        string addressLine,
        Guid? customerAddressId = null)
    {
        Guard.AgainstEmpty(orderId, nameof(orderId));
        if (customerAddressId == Guid.Empty)
        {
            throw new ArgumentException("The source address identifier cannot be empty.", nameof(customerAddressId));
        }

        OrderId = orderId;
        CustomerAddressId = customerAddressId;
        RecipientName = Guard.Required(recipientName, nameof(recipientName), 200);
        PhoneNumber = Guard.Required(phoneNumber, nameof(phoneNumber), 32);
        Province = Guard.Required(province, nameof(province), 100);
        City = Guard.Required(city, nameof(city), 100);
        PostalCode = Guard.Required(postalCode, nameof(postalCode), 20);
        AddressLine = Guard.Required(addressLine, nameof(addressLine), 1000);
    }

    public Guid OrderId { get; private set; }

    public Guid? CustomerAddressId { get; private set; }

    public string RecipientName { get; private set; } = string.Empty;

    public string PhoneNumber { get; private set; } = string.Empty;

    public string Province { get; private set; } = string.Empty;

    public string City { get; private set; } = string.Empty;

    public string PostalCode { get; private set; } = string.Empty;

    public string AddressLine { get; private set; } = string.Empty;
}

public sealed class OrderStoreSnapshot : AuditableEntity, IAppendOnlyEntity, IProtectedFromHardDelete
{
    private OrderStoreSnapshot()
    {
    }

    public OrderStoreSnapshot(
        Guid orderId,
        string tradeName,
        string legalName,
        string? nationalId,
        string? economicCode,
        string? registrationNumber,
        string phoneNumber,
        string postalCode,
        string addressLine)
    {
        Guard.AgainstEmpty(orderId, nameof(orderId));
        OrderId = orderId;
        TradeName = Guard.Required(tradeName, nameof(tradeName), 200);
        LegalName = Guard.Required(legalName, nameof(legalName), 200);
        NationalId = Guard.Optional(nationalId, nameof(nationalId), 32);
        EconomicCode = Guard.Optional(economicCode, nameof(economicCode), 32);
        RegistrationNumber = Guard.Optional(registrationNumber, nameof(registrationNumber), 32);
        PhoneNumber = Guard.Required(phoneNumber, nameof(phoneNumber), 32);
        PostalCode = Guard.Required(postalCode, nameof(postalCode), 20);
        AddressLine = Guard.Required(addressLine, nameof(addressLine), 1000);
    }

    public Guid OrderId { get; private set; }

    public string TradeName { get; private set; } = string.Empty;

    public string LegalName { get; private set; } = string.Empty;

    public string? NationalId { get; private set; }

    public string? EconomicCode { get; private set; }

    public string? RegistrationNumber { get; private set; }

    public string PhoneNumber { get; private set; } = string.Empty;

    public string PostalCode { get; private set; } = string.Empty;

    public string AddressLine { get; private set; } = string.Empty;
}
