using GoldInvoice.Application.Common;
using GoldInvoice.Domain.Inventory;

namespace GoldInvoice.Application.Inventory;

public sealed record WarehouseInfo(
    Guid Id,
    string Code,
    string Name,
    bool IsActive,
    string RowVersion);

public sealed record CreateWarehouseCommand(string Code, string Name);

public sealed record UpdateWarehouseCommand(
    string Code,
    string Name,
    bool IsActive,
    string RowVersion);

public sealed record InventoryItemInfo(
    Guid Id,
    Guid WarehouseId,
    Guid ProductVariantId,
    int QuantityOnHand,
    int QuantityReserved,
    int QuantityAvailable,
    string RowVersion);

public sealed record InventoryUnitInfo(
    Guid Id,
    Guid ProductId,
    Guid ProductVariantId,
    Guid WarehouseId,
    Guid InventoryItemId,
    string? SerialNumber,
    string? Barcode,
    decimal ActualGrossWeight,
    decimal ActualNetGoldWeight,
    int Karat,
    InventoryUnitStatus Status,
    DateTimeOffset ReceivedAt,
    DateTimeOffset? SoldAt,
    string RowVersion);

public sealed record StockReservationInfo(
    Guid Id,
    Guid InventoryItemId,
    Guid? InventoryUnitId,
    Guid OrderId,
    string ReservationKey,
    int Quantity,
    StockReservationStatus Status,
    DateTimeOffset ExpiresAt,
    string RowVersion);

public sealed record StockMovementInfo(
    Guid Id,
    Guid InventoryItemId,
    Guid? InventoryUnitId,
    StockMovementType MovementType,
    int QuantityDelta,
    int BalanceAfter,
    int ReservedQuantityDelta,
    int ReservedBalanceAfter,
    string? ReferenceType,
    Guid? ReferenceId,
    string? Reason,
    DateTimeOffset OccurredAt);

public sealed record ReceiveStockCommand(
    Guid WarehouseId,
    Guid ProductVariantId,
    int Quantity,
    string? ReferenceType,
    Guid? ReferenceId,
    string? Reason);

public sealed record AdjustStockCommand(
    Guid InventoryItemId,
    int QuantityDelta,
    string Reason,
    string RowVersion);

public sealed record ReceiveInventoryUnitCommand(
    Guid ProductId,
    Guid ProductVariantId,
    Guid WarehouseId,
    string? SerialNumber,
    string? Barcode,
    decimal ActualGrossWeight,
    decimal ActualNetGoldWeight,
    int Karat,
    long AcquisitionCostRials,
    DateTimeOffset? ReceivedAt);

public sealed record ReserveStockCommand(
    Guid InventoryItemId,
    Guid? InventoryUnitId,
    Guid OrderId,
    string ReservationKey,
    int Quantity,
    int LifetimeMinutes,
    string InventoryRowVersion);

public sealed record TransferStockCommand(
    Guid SourceInventoryItemId,
    Guid DestinationWarehouseId,
    int Quantity,
    string SourceRowVersion);

public sealed record TransferInventoryUnitCommand(
    Guid InventoryUnitId,
    Guid DestinationWarehouseId,
    string UnitRowVersion,
    string SourceInventoryRowVersion);

public interface IInventoryService
{
    Task<IReadOnlyList<WarehouseInfo>> GetWarehousesAsync(
        bool includeInactive,
        CancellationToken cancellationToken);

    Task<WarehouseInfo> GetWarehouseAsync(
        Guid warehouseId,
        CancellationToken cancellationToken);

    Task<WarehouseInfo> CreateWarehouseAsync(
        CreateWarehouseCommand command,
        CancellationToken cancellationToken);

    Task<WarehouseInfo> UpdateWarehouseAsync(
        Guid warehouseId,
        UpdateWarehouseCommand command,
        CancellationToken cancellationToken);

    Task<InventoryItemInfo> GetInventoryItemAsync(
        Guid inventoryItemId,
        CancellationToken cancellationToken);

    Task<InventoryUnitInfo> GetInventoryUnitAsync(
        Guid inventoryUnitId,
        CancellationToken cancellationToken);

    Task<InventoryUnitInfo> FindInventoryUnitAsync(
        string serialNumberOrBarcode,
        CancellationToken cancellationToken);

    Task<PagedResult<StockMovementInfo>> GetStockMovementsAsync(
        Guid inventoryItemId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<InventoryItemInfo> ReceiveStockAsync(
        ReceiveStockCommand command,
        CancellationToken cancellationToken);

    Task<InventoryItemInfo> AdjustStockAsync(
        AdjustStockCommand command,
        CancellationToken cancellationToken);

    Task<InventoryUnitInfo> ReceiveInventoryUnitAsync(
        ReceiveInventoryUnitCommand command,
        CancellationToken cancellationToken);

    Task<StockReservationInfo> ReserveAsync(
        ReserveStockCommand command,
        CancellationToken cancellationToken);

    Task<StockReservationInfo> ReleaseReservationAsync(
        Guid reservationId,
        string reservationRowVersion,
        string inventoryRowVersion,
        CancellationToken cancellationToken);

    Task<StockReservationInfo> ConfirmReservationAsync(
        Guid reservationId,
        string reservationRowVersion,
        string inventoryRowVersion,
        CancellationToken cancellationToken);

    Task TransferStockAsync(TransferStockCommand command, CancellationToken cancellationToken);

    Task<InventoryUnitInfo> TransferInventoryUnitAsync(
        TransferInventoryUnitCommand command,
        CancellationToken cancellationToken);

    Task<int> ExpireReservationsAsync(CancellationToken cancellationToken);
}
