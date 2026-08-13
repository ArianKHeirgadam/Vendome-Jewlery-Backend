using GoldInvoice.Domain.Catalog;
using GoldInvoice.Domain.Common;

namespace GoldInvoice.Domain.Inventory;

public enum StockMovementType
{
    InitialStock,
    Purchase,
    Reservation,
    ReservationReleased,
    ReservationConfirmed,
    Sale,
    Return,
    TransferOut,
    TransferIn,
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

public enum InventoryUnitStatus
{
    Available,
    Reserved,
    Sold,
    Damaged,
    Returned,
    Transferred,
    Inactive
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

    public void Update(string code, string name, bool isActive)
    {
        Code = Guard.Required(code, nameof(code), 50).ToUpperInvariant();
        Name = Guard.Required(name, nameof(name), 200);
        IsActive = isActive;
    }
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

    public long AverageUnitCostRials { get; private set; }

    public bool HasAcquisitionCost { get; private set; }

    public int QuantityAvailable => QuantityOnHand - QuantityReserved;

    public void Receive(int quantity)
    {
        Guard.AgainstNonPositive(quantity, nameof(quantity));
        QuantityOnHand = checked(QuantityOnHand + quantity);
    }

    public void ReceivePurchase(int quantity, long unitCostRials)
    {
        Guard.AgainstNonPositive(quantity, nameof(quantity));
        Guard.AgainstNegative(unitCostRials, nameof(unitCostRials));

        var resultingQuantity = checked(QuantityOnHand + quantity);
        if (!HasAcquisitionCost)
        {
            // Legacy/initial stock has no trustworthy cost. The first documented
            // supplier purchase becomes the best available estimate for the pool.
            AverageUnitCostRials = unitCostRials;
            HasAcquisitionCost = true;
        }
        else
        {
            var existingValue = checked((decimal)QuantityOnHand * AverageUnitCostRials);
            var receivedValue = checked((decimal)quantity * unitCostRials);
            AverageUnitCostRials = checked((long)decimal.Round(
                (existingValue + receivedValue) / resultingQuantity,
                0,
                MidpointRounding.AwayFromZero));
        }

        QuantityOnHand = resultingQuantity;
    }

    public void Adjust(int quantityDelta)
    {
        if (quantityDelta == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantityDelta));
        }

        var resultingQuantity = checked(QuantityOnHand + quantityDelta);
        if (resultingQuantity < QuantityReserved)
        {
            throw new DomainConflictException("Stock cannot be reduced below its reserved quantity.");
        }

        QuantityOnHand = resultingQuantity;
    }

    public void Reserve(int quantity)
    {
        Guard.AgainstNonPositive(quantity, nameof(quantity));
        if (quantity > QuantityAvailable)
        {
            throw new DomainConflictException("The requested quantity is not available.");
        }

        QuantityReserved = checked(QuantityReserved + quantity);
    }

    public void ReleaseReservation(int quantity)
    {
        Guard.AgainstNonPositive(quantity, nameof(quantity));
        if (quantity > QuantityReserved)
        {
            throw new DomainConflictException("The released quantity exceeds reserved stock.");
        }

        QuantityReserved -= quantity;
    }

    public void ConfirmReservation(int quantity)
    {
        Guard.AgainstNonPositive(quantity, nameof(quantity));
        if (quantity > QuantityReserved || quantity > QuantityOnHand)
        {
            throw new DomainConflictException("The confirmed quantity is not reserved and available.");
        }

        QuantityReserved -= quantity;
        QuantityOnHand -= quantity;
    }
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
        DateTimeOffset occurredAt,
        int reservedQuantityDelta = 0,
        int reservedBalanceAfter = 0,
        Guid? inventoryUnitId = null)
    {
        Guard.AgainstEmpty(inventoryItemId, nameof(inventoryItemId));
        Guard.AgainstNegative(balanceAfter, nameof(balanceAfter));
        Guard.AgainstNegative(reservedBalanceAfter, nameof(reservedBalanceAfter));
        Guard.AgainstDefault(occurredAt, nameof(occurredAt));
        if (quantityDelta == 0 && reservedQuantityDelta == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantityDelta), "A stock movement must change stock or reservations.");
        }

        if (reservedBalanceAfter > balanceAfter)
        {
            throw new ArgumentException("Reserved stock cannot exceed on-hand stock.", nameof(reservedBalanceAfter));
        }

        InventoryItemId = inventoryItemId;
        InventoryUnitId = inventoryUnitId;
        MovementType = movementType;
        QuantityDelta = quantityDelta;
        BalanceAfter = balanceAfter;
        ReservedQuantityDelta = reservedQuantityDelta;
        ReservedBalanceAfter = reservedBalanceAfter;
        OccurredAt = occurredAt;
    }

    public Guid InventoryItemId { get; private set; }

    public Guid? InventoryUnitId { get; private set; }

    public StockMovementType MovementType { get; private set; }

    public int QuantityDelta { get; private set; }

    public int BalanceAfter { get; private set; }

    public int ReservedQuantityDelta { get; private set; }

    public int ReservedBalanceAfter { get; private set; }

    public string? ReferenceType { get; private set; }

    public Guid? ReferenceId { get; private set; }

    public string? Reason { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public void SetReference(string? referenceType, Guid? referenceId, string? reason)
    {
        if (referenceId == Guid.Empty)
        {
            throw new ArgumentException("A non-empty reference identifier is required.", nameof(referenceId));
        }

        ReferenceType = Guard.Optional(referenceType, nameof(referenceType), 100);
        ReferenceId = referenceId;
        Reason = Guard.Optional(reason, nameof(reason), 1000);
    }
}

public sealed class StockReservation : AuditableEntity, IProtectedFromHardDelete
{
    private StockReservation()
    {
    }

    public StockReservation(
        Guid inventoryItemId,
        Guid orderId,
        string reservationKey,
        int quantity,
        DateTimeOffset expiresAt,
        Guid? inventoryUnitId = null,
        Guid? orderItemId = null)
    {
        Guard.AgainstEmpty(inventoryItemId, nameof(inventoryItemId));
        Guard.AgainstEmpty(orderId, nameof(orderId));
        Guard.AgainstNonPositive(quantity, nameof(quantity));
        Guard.AgainstDefault(expiresAt, nameof(expiresAt));
        if (inventoryUnitId == Guid.Empty)
        {
            throw new ArgumentException("A non-empty inventory-unit identifier is required.", nameof(inventoryUnitId));
        }

        if (orderItemId == Guid.Empty)
        {
            throw new ArgumentException("A non-empty order-item identifier is required.", nameof(orderItemId));
        }

        if (inventoryUnitId is not null && quantity != 1)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "A physical-unit reservation must have quantity one.");
        }

        InventoryItemId = inventoryItemId;
        InventoryUnitId = inventoryUnitId;
        OrderId = orderId;
        OrderItemId = orderItemId;
        ReservationKey = Guard.Required(reservationKey, nameof(reservationKey), 128);
        Quantity = quantity;
        ExpiresAt = expiresAt;
    }

    public Guid InventoryItemId { get; private set; }

    public Guid? InventoryUnitId { get; private set; }

    public Guid OrderId { get; private set; }

    public Guid? OrderItemId { get; private set; }

    public string ReservationKey { get; private set; } = string.Empty;

    public int Quantity { get; private set; }

    public StockReservationStatus Status { get; private set; } = StockReservationStatus.Active;

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? ConfirmedAt { get; private set; }

    public DateTimeOffset? ReleasedAt { get; private set; }

    public void Confirm(DateTimeOffset confirmedAt)
    {
        Guard.AgainstDefault(confirmedAt, nameof(confirmedAt));
        EnsureActive();
        if (confirmedAt >= ExpiresAt)
        {
            throw new DomainConflictException("An expired stock reservation cannot be confirmed.");
        }

        Status = StockReservationStatus.Confirmed;
        ConfirmedAt = confirmedAt;
    }

    public void Release(DateTimeOffset releasedAt)
    {
        Guard.AgainstDefault(releasedAt, nameof(releasedAt));
        EnsureActive();
        Status = StockReservationStatus.Released;
        ReleasedAt = releasedAt;
    }

    public void Expire(DateTimeOffset expiredAt)
    {
        Guard.AgainstDefault(expiredAt, nameof(expiredAt));
        EnsureActive();
        if (expiredAt < ExpiresAt)
        {
            throw new ArgumentOutOfRangeException(nameof(expiredAt), "A reservation cannot expire before its deadline.");
        }

        Status = StockReservationStatus.Expired;
        ReleasedAt = expiredAt;
    }

    private void EnsureActive()
    {
        if (Status != StockReservationStatus.Active)
        {
            throw new DomainConflictException("Only an active reservation can change state.");
        }
    }
}

public sealed class InventoryUnit : AuditableEntity, IProtectedFromHardDelete
{
    private InventoryUnit()
    {
    }

    public InventoryUnit(
        Guid productId,
        Guid productVariantId,
        Guid warehouseId,
        Guid inventoryItemId,
        string? serialNumber,
        string? barcode,
        decimal actualGrossWeight,
        decimal actualNetGoldWeight,
        int karat,
        long acquisitionCostRials,
        DateTimeOffset receivedAt)
    {
        Guard.AgainstEmpty(productId, nameof(productId));
        Guard.AgainstEmpty(productVariantId, nameof(productVariantId));
        Guard.AgainstEmpty(warehouseId, nameof(warehouseId));
        Guard.AgainstEmpty(inventoryItemId, nameof(inventoryItemId));
        Guard.AgainstNonPositive(actualGrossWeight, nameof(actualGrossWeight));
        Guard.AgainstNonPositive(actualNetGoldWeight, nameof(actualNetGoldWeight));
        Guard.AgainstNegative(acquisitionCostRials, nameof(acquisitionCostRials));
        Guard.AgainstDefault(receivedAt, nameof(receivedAt));

        if (!GoldProductDetail.IsSupportedKarat(karat))
        {
            throw new ArgumentOutOfRangeException(nameof(karat));
        }

        if (actualNetGoldWeight > actualGrossWeight)
        {
            throw new ArgumentException("Net gold weight cannot exceed gross weight.", nameof(actualNetGoldWeight));
        }

        ProductId = productId;
        ProductVariantId = productVariantId;
        WarehouseId = warehouseId;
        InventoryItemId = inventoryItemId;
        SerialNumber = Guard.Optional(serialNumber, nameof(serialNumber), 100)?.ToUpperInvariant();
        Barcode = Guard.Optional(barcode, nameof(barcode), 100)?.ToUpperInvariant();
        ActualGrossWeight = actualGrossWeight;
        ActualNetGoldWeight = actualNetGoldWeight;
        Karat = karat;
        AcquisitionCostRials = acquisitionCostRials;
        ReceivedAt = receivedAt;
    }

    public Guid ProductId { get; private set; }

    public Guid ProductVariantId { get; private set; }

    public Guid WarehouseId { get; private set; }

    public Guid InventoryItemId { get; private set; }

    public string? SerialNumber { get; private set; }

    public string? Barcode { get; private set; }

    public decimal ActualGrossWeight { get; private set; }

    public decimal ActualNetGoldWeight { get; private set; }

    public int Karat { get; private set; }

    public long AcquisitionCostRials { get; private set; }

    public InventoryUnitStatus Status { get; private set; } = InventoryUnitStatus.Available;

    public DateTimeOffset ReceivedAt { get; private set; }

    public DateTimeOffset? SoldAt { get; private set; }

    public void Reserve()
    {
        EnsureStatus(InventoryUnitStatus.Available);
        Status = InventoryUnitStatus.Reserved;
    }

    public void ReleaseReservation()
    {
        EnsureStatus(InventoryUnitStatus.Reserved);
        Status = InventoryUnitStatus.Available;
    }

    public void Sell(DateTimeOffset soldAt)
    {
        Guard.AgainstDefault(soldAt, nameof(soldAt));
        if (Status is not InventoryUnitStatus.Reserved and not InventoryUnitStatus.Available)
        {
            throw new DomainConflictException("Only an available or reserved unit can be sold.");
        }

        Status = InventoryUnitStatus.Sold;
        SoldAt = soldAt;
    }

    public void Return()
    {
        EnsureStatus(InventoryUnitStatus.Sold);
        Status = InventoryUnitStatus.Returned;
    }

    public void RestoreToAvailable()
    {
        if (Status is not InventoryUnitStatus.Returned and not InventoryUnitStatus.Damaged)
        {
            throw new DomainConflictException("Only a returned or repaired unit can become available.");
        }

        Status = InventoryUnitStatus.Available;
    }

    public void MarkDamaged()
    {
        if (Status is InventoryUnitStatus.Sold or InventoryUnitStatus.Inactive)
        {
            throw new DomainConflictException("This inventory unit cannot be marked as damaged.");
        }

        Status = InventoryUnitStatus.Damaged;
    }

    public void TransferTo(Guid warehouseId, Guid inventoryItemId)
    {
        Guard.AgainstEmpty(warehouseId, nameof(warehouseId));
        Guard.AgainstEmpty(inventoryItemId, nameof(inventoryItemId));
        EnsureStatus(InventoryUnitStatus.Available);
        WarehouseId = warehouseId;
        InventoryItemId = inventoryItemId;
    }

    public void Deactivate()
    {
        if (Status is InventoryUnitStatus.Reserved or InventoryUnitStatus.Sold)
        {
            throw new DomainConflictException("A reserved or sold unit cannot be deactivated.");
        }

        Status = InventoryUnitStatus.Inactive;
    }

    private void EnsureStatus(InventoryUnitStatus expectedStatus)
    {
        if (Status != expectedStatus)
        {
            throw new DomainConflictException($"The inventory unit must be {expectedStatus}.");
        }
    }
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
