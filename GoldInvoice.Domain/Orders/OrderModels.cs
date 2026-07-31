using GoldInvoice.Domain.Common;

namespace GoldInvoice.Domain.Orders;

public enum OrderStatus
{
    Pending,
    AwaitingPayment,
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
        long shippingRials)
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
    }

    public Guid CustomerId { get; private set; }

    public string OrderNumber { get; private set; } = string.Empty;

    public OrderStatus Status { get; private set; } = OrderStatus.Pending;

    public long ItemsSubtotalRials { get; private set; }

    public long DiscountRials { get; private set; }

    public long ShippingRials { get; private set; }

    public long GrandTotalRials { get; private set; }

    public DateTimeOffset? PaidAt { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }
}

public sealed class OrderItem : AuditableEntity, IProtectedFromHardDelete
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
        int quantity)
    {
        Guard.AgainstEmpty(orderId, nameof(orderId));
        Guard.AgainstEmpty(productVariantId, nameof(productVariantId));
        Guard.AgainstNonPositive(lineNumber, nameof(lineNumber));
        Guard.AgainstNonPositive(weightGrams, nameof(weightGrams));
        Guard.AgainstOutOfRange(purity, 1, 1000, nameof(purity));
        Guard.AgainstNegative(unitPriceRials, nameof(unitPriceRials));
        Guard.AgainstNonPositive(quantity, nameof(quantity));
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
    }

    public Guid OrderId { get; private set; }

    public Guid ProductVariantId { get; private set; }

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

public sealed class OrderStatusHistory : AuditableEntity, IAppendOnlyEntity, IProtectedFromHardDelete
{
    private OrderStatusHistory()
    {
    }

    public OrderStatusHistory(Guid orderId, OrderStatus? fromStatus, OrderStatus toStatus, DateTimeOffset occurredAt)
    {
        Guard.AgainstEmpty(orderId, nameof(orderId));
        Guard.AgainstDefault(occurredAt, nameof(occurredAt));
        OrderId = orderId;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        OccurredAt = occurredAt;
    }

    public Guid OrderId { get; private set; }

    public OrderStatus? FromStatus { get; private set; }

    public OrderStatus ToStatus { get; private set; }

    public Guid? ChangedBy { get; private set; }

    public string? Reason { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }
}

public sealed class OrderAddressSnapshot : AuditableEntity, IProtectedFromHardDelete
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
        string addressLine)
    {
        Guard.AgainstEmpty(orderId, nameof(orderId));
        OrderId = orderId;
        RecipientName = Guard.Required(recipientName, nameof(recipientName), 200);
        PhoneNumber = Guard.Required(phoneNumber, nameof(phoneNumber), 32);
        Province = Guard.Required(province, nameof(province), 100);
        City = Guard.Required(city, nameof(city), 100);
        PostalCode = Guard.Required(postalCode, nameof(postalCode), 20);
        AddressLine = Guard.Required(addressLine, nameof(addressLine), 1000);
    }

    public Guid OrderId { get; private set; }

    public string RecipientName { get; private set; } = string.Empty;

    public string PhoneNumber { get; private set; } = string.Empty;

    public string Province { get; private set; } = string.Empty;

    public string City { get; private set; } = string.Empty;

    public string PostalCode { get; private set; } = string.Empty;

    public string AddressLine { get; private set; } = string.Empty;
}
