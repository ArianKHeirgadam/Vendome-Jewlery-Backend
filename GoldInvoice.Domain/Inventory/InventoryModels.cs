using GoldInvoice.Domain.Common;

namespace GoldInvoice.Domain.Inventory;

public enum StockMovementType
{
    InitialStock,
    Purchase,
    Reservation,
    ReservationReleased,
    Sale,
    Return,
    ManualAdjustment,
    Damage,
    Correction
}

public enum StockReservationStatus
{
    Active,
    Confirmed,
    Released,
    Expired
}

public sealed class Warehouse : SoftDeletableEntity
{
    private Warehouse()
    {
    }

    public Warehouse(string code, string name)
    {
        Code = Guard.Required(code, nameof(code), 50).ToUpperInvariant();
        Name = Guard.Required(name, nameof(name), 200);
    }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public bool IsActive { get; private set; } = true;
}

public sealed class InventoryItem : AuditableEntity
{
    private InventoryItem()
    {
    }

    public InventoryItem(Guid warehouseId, Guid productVariantId, int quantityOnHand = 0)
    {
        Guard.AgainstEmpty(warehouseId, nameof(warehouseId));
        Guard.AgainstEmpty(productVariantId, nameof(productVariantId));
        Guard.AgainstNegative(quantityOnHand, nameof(quantityOnHand));
        WarehouseId = warehouseId;
        ProductVariantId = productVariantId;
        QuantityOnHand = quantityOnHand;
    }

    public Guid WarehouseId { get; private set; }

    public Guid ProductVariantId { get; private set; }

    public int QuantityOnHand { get; private set; }

    public int QuantityReserved { get; private set; }
}

public sealed class StockMovement : AuditableEntity, IAppendOnlyEntity, IProtectedFromHardDelete
{
    private StockMovement()
    {
    }

    public StockMovement(
        Guid inventoryItemId,
        StockMovementType movementType,
        int quantityDelta,
        int balanceAfter,
        DateTimeOffset occurredAt)
    {
        Guard.AgainstEmpty(inventoryItemId, nameof(inventoryItemId));
        Guard.AgainstNegative(balanceAfter, nameof(balanceAfter));
        Guard.AgainstDefault(occurredAt, nameof(occurredAt));
        if (quantityDelta == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantityDelta), "A stock movement cannot have a zero quantity.");
        }

        InventoryItemId = inventoryItemId;
        MovementType = movementType;
        QuantityDelta = quantityDelta;
        BalanceAfter = balanceAfter;
        OccurredAt = occurredAt;
    }

    public Guid InventoryItemId { get; private set; }

    public StockMovementType MovementType { get; private set; }

    public int QuantityDelta { get; private set; }

    public int BalanceAfter { get; private set; }

    public string? ReferenceType { get; private set; }

    public Guid? ReferenceId { get; private set; }

    public string? Reason { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }
}

public sealed class StockReservation : AuditableEntity
{
    private StockReservation()
    {
    }

    public StockReservation(
        Guid inventoryItemId,
        Guid orderId,
        string reservationKey,
        int quantity,
        DateTimeOffset expiresAt)
    {
        Guard.AgainstEmpty(inventoryItemId, nameof(inventoryItemId));
        Guard.AgainstEmpty(orderId, nameof(orderId));
        Guard.AgainstNonPositive(quantity, nameof(quantity));
        Guard.AgainstDefault(expiresAt, nameof(expiresAt));
        InventoryItemId = inventoryItemId;
        OrderId = orderId;
        ReservationKey = Guard.Required(reservationKey, nameof(reservationKey), 128);
        Quantity = quantity;
        ExpiresAt = expiresAt;
    }

    public Guid InventoryItemId { get; private set; }

    public Guid OrderId { get; private set; }

    public string ReservationKey { get; private set; } = string.Empty;

    public int Quantity { get; private set; }

    public StockReservationStatus Status { get; private set; } = StockReservationStatus.Active;

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? ConfirmedAt { get; private set; }

    public DateTimeOffset? ReleasedAt { get; private set; }
}

public sealed class InventoryAdjustment : AuditableEntity, IAppendOnlyEntity, IProtectedFromHardDelete
{
    private InventoryAdjustment()
    {
    }

    public InventoryAdjustment(
        Guid inventoryItemId,
        Guid stockMovementId,
        int quantityDelta,
        string reason)
    {
        Guard.AgainstEmpty(inventoryItemId, nameof(inventoryItemId));
        Guard.AgainstEmpty(stockMovementId, nameof(stockMovementId));
        if (quantityDelta == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantityDelta));
        }

        InventoryItemId = inventoryItemId;
        StockMovementId = stockMovementId;
        QuantityDelta = quantityDelta;
        Reason = Guard.Required(reason, nameof(reason), 1000);
    }

    public Guid InventoryItemId { get; private set; }

    public Guid StockMovementId { get; private set; }

    public int QuantityDelta { get; private set; }

    public string Reason { get; private set; } = string.Empty;

    public Guid? ApprovedBy { get; private set; }
}
