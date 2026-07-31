using System.ComponentModel.DataAnnotations;

namespace GoldInvoice.Contracts.Inventory;

public sealed class WarehouseResponse
{
    public required Guid Id { get; init; }
    public required string Code { get; init; }
    public required string Name { get; init; }
    public required bool IsActive { get; init; }
    public required string RowVersion { get; init; }
}

public class CreateWarehouseRequest
{
    [Required, StringLength(50)]
    public string Code { get; init; } = string.Empty;

    [Required, StringLength(200)]
    public string Name { get; init; } = string.Empty;
}

public sealed class UpdateWarehouseRequest : CreateWarehouseRequest
{
    public bool IsActive { get; init; } = true;

    [Required]
    public string RowVersion { get; init; } = string.Empty;
}

public sealed class InventoryItemResponse
{
    public required Guid Id { get; init; }
    public required Guid WarehouseId { get; init; }
    public required Guid ProductVariantId { get; init; }
    public required int QuantityOnHand { get; init; }
    public required int QuantityReserved { get; init; }
    public required int QuantityAvailable { get; init; }
    public required string RowVersion { get; init; }
}

public sealed class InventoryUnitResponse
{
    public required Guid Id { get; init; }
    public required Guid ProductId { get; init; }
    public required Guid ProductVariantId { get; init; }
    public required Guid WarehouseId { get; init; }
    public required Guid InventoryItemId { get; init; }
    public string? SerialNumber { get; init; }
    public string? Barcode { get; init; }
    public required decimal ActualGrossWeight { get; init; }
    public required decimal ActualNetGoldWeight { get; init; }
    public required int Karat { get; init; }
    public required string Status { get; init; }
    public required DateTimeOffset ReceivedAt { get; init; }
    public DateTimeOffset? SoldAt { get; init; }
    public required string RowVersion { get; init; }
}

public sealed class StockReservationResponse
{
    public required Guid Id { get; init; }
    public required Guid InventoryItemId { get; init; }
    public Guid? InventoryUnitId { get; init; }
    public required Guid OrderId { get; init; }
    public required string ReservationKey { get; init; }
    public required int Quantity { get; init; }
    public required string Status { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public required string RowVersion { get; init; }
}

public sealed class StockMovementResponse
{
    public required Guid Id { get; init; }
    public required Guid InventoryItemId { get; init; }
    public Guid? InventoryUnitId { get; init; }
    public required string MovementType { get; init; }
    public required int QuantityDelta { get; init; }
    public required int BalanceAfter { get; init; }
    public required int ReservedQuantityDelta { get; init; }
    public required int ReservedBalanceAfter { get; init; }
    public string? ReferenceType { get; init; }
    public Guid? ReferenceId { get; init; }
    public string? Reason { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
}

public sealed class ReceiveStockRequest
{
    public Guid WarehouseId { get; init; }
    public Guid ProductVariantId { get; init; }

    [Range(1, int.MaxValue)]
    public int Quantity { get; init; }

    [StringLength(100)]
    public string? ReferenceType { get; init; }

    public Guid? ReferenceId { get; init; }

    [StringLength(1000)]
    public string? Reason { get; init; }
}

public sealed class AdjustStockRequest
{
    public int QuantityDelta { get; init; }

    [Required, StringLength(1000)]
    public string Reason { get; init; } = string.Empty;

    [Required]
    public string RowVersion { get; init; } = string.Empty;
}

public sealed class ReceiveInventoryUnitRequest
{
    public Guid ProductId { get; init; }
    public Guid ProductVariantId { get; init; }
    public Guid WarehouseId { get; init; }

    [StringLength(100)]
    public string? SerialNumber { get; init; }

    [StringLength(100)]
    public string? Barcode { get; init; }

    public decimal ActualGrossWeight { get; init; }
    public decimal ActualNetGoldWeight { get; init; }
    public int Karat { get; init; }
    public long AcquisitionCostRials { get; init; }
    public DateTimeOffset? ReceivedAt { get; init; }
}

public sealed class ReserveStockRequest
{
    public Guid InventoryItemId { get; init; }
    public Guid? InventoryUnitId { get; init; }
    public Guid OrderId { get; init; }

    [Required, StringLength(128)]
    public string ReservationKey { get; init; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int Quantity { get; init; }

    [Range(1, 1440)]
    public int LifetimeMinutes { get; init; } = 15;

    [Required]
    public string InventoryRowVersion { get; init; } = string.Empty;
}

public sealed class ChangeReservationRequest
{
    [Required]
    public string ReservationRowVersion { get; init; } = string.Empty;

    [Required]
    public string InventoryRowVersion { get; init; } = string.Empty;
}

public sealed class TransferStockRequest
{
    public Guid SourceInventoryItemId { get; init; }
    public Guid DestinationWarehouseId { get; init; }

    [Range(1, int.MaxValue)]
    public int Quantity { get; init; }

    [Required]
    public string SourceRowVersion { get; init; } = string.Empty;
}

public sealed class TransferInventoryUnitRequest
{
    public Guid InventoryUnitId { get; init; }
    public Guid DestinationWarehouseId { get; init; }

    [Required]
    public string UnitRowVersion { get; init; } = string.Empty;

    [Required]
    public string SourceInventoryRowVersion { get; init; } = string.Empty;
}
