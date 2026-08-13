using System.ComponentModel.DataAnnotations;
using GoldInvoice.Contracts.Orders;

namespace GoldInvoice.Contracts.Invoicing;

public sealed class InvoiceItemResponse
{
    public required Guid Id { get; init; }
    public Guid? OrderItemId { get; init; }
    public Guid? PriceCalculationSnapshotId { get; init; }
    public Guid? InventoryUnitId { get; init; }
    public required int LineNumber { get; init; }
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
    public long? AcquisitionUnitCostRials { get; init; }
    public long? AcquisitionTotalCostRials { get; init; }
    public long? GrossProfitRials { get; init; }
    public string? RoundingPolicy { get; init; }
}

public sealed class InvoiceAddressSnapshotResponse
{
    public required Guid Id { get; init; }
    public required Guid OrderAddressSnapshotId { get; init; }
    public required string RecipientName { get; init; }
    public required string PhoneNumber { get; init; }
    public required string Province { get; init; }
    public required string City { get; init; }
    public required string PostalCode { get; init; }
    public required string AddressLine { get; init; }
}

public sealed class InvoiceResponse
{
    public required Guid Id { get; init; }
    public required Guid OrderId { get; init; }
    public required Guid CustomerId { get; init; }
    public Guid? PaymentId { get; init; }
    public required string InvoiceNumber { get; init; }
    public required string Status { get; init; }
    public required DateTimeOffset IssuedAt { get; init; }
    public required long SubtotalRials { get; init; }
    public required long DiscountRials { get; init; }
    public required long ShippingRials { get; init; }
    public required long GrandTotalRials { get; init; }
    public string? CustomerNameSnapshot { get; init; }
    public string? CustomerNationalIdSnapshot { get; init; }
    public DateTimeOffset? VoidedAt { get; init; }
    public string? VoidReason { get; init; }
    public InvoiceAddressSnapshotResponse? Address { get; init; }
    public StoreIdentitySnapshotResponse? Store { get; init; }
    public required IReadOnlyList<InvoiceItemResponse> Items { get; init; }
    public required string RowVersion { get; init; }
}

public sealed class VoidInvoiceRequest
{
    [Required, StringLength(1000)]
    public string Reason { get; init; } = string.Empty;

    [Required, StringLength(256)]
    public string RowVersion { get; init; } = string.Empty;
}

public sealed class CorrectInvoiceDocumentRequest
{
    [Required, StringLength(200)]
    public string CustomerName { get; init; } = string.Empty;

    [StringLength(32)]
    public string? CustomerNationalId { get; init; }

    [Required, StringLength(200)]
    public string RecipientName { get; init; } = string.Empty;

    [Required, StringLength(32)]
    public string PhoneNumber { get; init; } = string.Empty;

    [Required, StringLength(100)]
    public string Province { get; init; } = string.Empty;

    [Required, StringLength(100)]
    public string City { get; init; } = string.Empty;

    [Required, StringLength(20)]
    public string PostalCode { get; init; } = string.Empty;

    [Required, StringLength(1000)]
    public string AddressLine { get; init; } = string.Empty;

    [Required, StringLength(1000, MinimumLength = 3)]
    public string Reason { get; init; } = string.Empty;

    [Required, StringLength(256)]
    public string RowVersion { get; init; } = string.Empty;
}

public sealed class RequestInvoicePrintRequest
{
    [Range(1, 20)]
    public int Copies { get; init; } = 1;

    [StringLength(1000)]
    public string? ReprintReason { get; init; }
}

public sealed class CompleteInvoicePrintRequest
{
    public required bool Succeeded { get; init; }

    [StringLength(300)]
    public string? PrinterName { get; init; }

    [StringLength(100)]
    public string? FailureCode { get; init; }

    [Required, StringLength(256)]
    public string RowVersion { get; init; } = string.Empty;
}

public sealed class InvoicePrintResponse
{
    public required Guid Id { get; init; }
    public required Guid InvoiceId { get; init; }
    public required Guid RequestedByUserId { get; init; }
    public required string Status { get; init; }
    public required int Copies { get; init; }
    public required bool IsReprint { get; init; }
    public string? ReprintReason { get; init; }
    public string? PrinterName { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public string? FailureCode { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required string RowVersion { get; init; }
}
