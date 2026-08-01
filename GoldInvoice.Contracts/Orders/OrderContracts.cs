using System.ComponentModel.DataAnnotations;

namespace GoldInvoice.Contracts.Orders;

public sealed class OrderAddressSnapshotResponse
{
    public required Guid Id { get; init; }
    public Guid? CustomerAddressId { get; init; }
    public required string RecipientName { get; init; }
    public required string PhoneNumber { get; init; }
    public required string Province { get; init; }
    public required string City { get; init; }
    public required string PostalCode { get; init; }
    public required string AddressLine { get; init; }
}

public sealed class StoreIdentitySnapshotResponse
{
    public required Guid Id { get; init; }
    public required string TradeName { get; init; }
    public required string LegalName { get; init; }
    public string? NationalId { get; init; }
    public string? EconomicCode { get; init; }
    public string? RegistrationNumber { get; init; }
    public required string PhoneNumber { get; init; }
    public required string PostalCode { get; init; }
    public required string AddressLine { get; init; }
}

public sealed class OrderItemResponse
{
    public required Guid Id { get; init; }
    public required int LineNumber { get; init; }
    public required Guid ProductVariantId { get; init; }
    public Guid? InventoryItemId { get; init; }
    public Guid? InventoryUnitId { get; init; }
    public Guid? PriceCalculationSnapshotId { get; init; }
    public Guid? StockReservationId { get; init; }
    public required string Sku { get; init; }
    public required string ProductName { get; init; }
    public required string VariantName { get; init; }
    public required decimal GrossWeightGrams { get; init; }
    public decimal? NetGoldWeightGrams { get; init; }
    public int? Karat { get; init; }
    public required int Quantity { get; init; }
    public long? MarketUnitPriceRials { get; init; }
    public long? GoldValueRials { get; init; }
    public long? WageRials { get; init; }
    public long? ProfitRials { get; init; }
    public long? TaxRials { get; init; }
    public required long UnitPriceRials { get; init; }
    public required long LineTotalRials { get; init; }
    public string? RoundingPolicy { get; init; }
}

public sealed class OrderResponse
{
    public required Guid Id { get; init; }
    public required Guid CustomerId { get; init; }
    public required string OrderNumber { get; init; }
    public required string Status { get; init; }
    public required long ItemsSubtotalRials { get; init; }
    public required long DiscountRials { get; init; }
    public required long ShippingRials { get; init; }
    public required long GrandTotalRials { get; init; }
    public string? CustomerNameSnapshot { get; init; }
    public string? CustomerNationalIdSnapshot { get; init; }
    public DateTimeOffset? PaidAt { get; init; }
    public DateTimeOffset? CancelledAt { get; init; }
    public OrderAddressSnapshotResponse? Address { get; init; }
    public StoreIdentitySnapshotResponse? Store { get; init; }
    public required IReadOnlyList<OrderItemResponse> Items { get; init; }
    public required string RowVersion { get; init; }
}

public sealed class CreateOrderLineRequest
{
    public Guid InventoryItemId { get; init; }
    public Guid? InventoryUnitId { get; init; }

    [Range(1, 1000)]
    public int Quantity { get; init; }

    [Range(typeof(decimal), "0.001", "999999999999999.999")]
    public decimal? ActualGrossWeight { get; init; }

    [Range(typeof(decimal), "0.001", "999999999999999.999")]
    public decimal? ActualNetGoldWeight { get; init; }

    [Required, StringLength(256)]
    public string InventoryRowVersion { get; init; } = string.Empty;

    [StringLength(256)]
    public string? InventoryUnitRowVersion { get; init; }
}

public sealed class CreateOrderRequest
{
    public Guid CustomerId { get; init; }
    public Guid CustomerAddressId { get; init; }

    [StringLength(32)]
    public string? CustomerNationalId { get; init; }

    [Required, MinLength(1), MaxLength(100)]
    public IReadOnlyList<CreateOrderLineRequest> Lines { get; init; } = [];

    [Range(1, 60)]
    public int ReservationLifetimeMinutes { get; init; } = 15;

    [Range(typeof(long), "0", "9223372036854775807")]
    public long DiscountRials { get; init; }

    [Range(typeof(long), "0", "9223372036854775807")]
    public long ShippingRials { get; init; }
}

public sealed class CancelOrderRequest
{
    [Required, StringLength(1000)]
    public string Reason { get; init; } = string.Empty;

    [Required, StringLength(256)]
    public string RowVersion { get; init; } = string.Empty;
}

public sealed class ChangeOrderStatusRequest
{
    [Required, StringLength(50)]
    public string TargetStatus { get; init; } = string.Empty;

    [StringLength(1000)]
    public string? Reason { get; init; }

    [Required, StringLength(256)]
    public string RowVersion { get; init; } = string.Empty;
}
