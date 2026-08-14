using GoldInvoice.Application.Common;
using GoldInvoice.Application.Integration;
using GoldInvoice.Domain.Inventory;
using GoldInvoice.Domain.Orders;
using GoldInvoice.Infrastructure.Integration;
using GoldInvoice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoldInvoice.Infrastructure.Inventory;

internal sealed class InventoryReservationCoordinator(
    GoldInvoiceDbContext dbContext,
    IOutboxWriter outboxWriter,
    TimeProvider timeProvider)
{
    public StockReservation Reserve(
        OrderItem orderItem,
        InventoryItem inventoryItem,
        InventoryUnit? inventoryUnit,
        int quantity,
        DateTimeOffset expiresAt)
    {
        ArgumentNullException.ThrowIfNull(orderItem);
        ArgumentNullException.ThrowIfNull(inventoryItem);
        if (inventoryUnit is not null && inventoryUnit.InventoryItemId != inventoryItem.Id)
        {
            throw new ApplicationConflictException();
        }

        inventoryItem.Reserve(quantity);
        inventoryUnit?.Reserve();
        var reservation = new StockReservation(
            inventoryItem.Id,
            orderItem.OrderId,
            $"ORDER:{orderItem.OrderId:N}:LINE:{orderItem.LineNumber:D3}",
            quantity,
            expiresAt,
            inventoryUnit?.Id,
            orderItem.Id);
        var movement = CreateMovement(
            inventoryItem,
            StockMovementType.Reservation,
            quantityDelta: 0,
            reservedQuantityDelta: quantity,
            inventoryUnit?.Id,
            "OrderItem",
            orderItem.Id,
            reason: null);
        dbContext.StockReservations.Add(reservation);
        dbContext.StockMovements.Add(movement);
        outboxWriter.AddInventoryChanged(inventoryItem, movement);
        return reservation;
    }

    public async Task EnsurePayableAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var orderItems = await dbContext.OrderItems
            .AsNoTracking()
            .Where(item => item.OrderId == orderId && item.PriceCalculationSnapshotId != null)
            .ToListAsync(cancellationToken);
        var reservations = await dbContext.StockReservations
            .AsNoTracking()
            .Where(reservation => reservation.OrderId == orderId && reservation.OrderItemId != null)
            .ToListAsync(cancellationToken);
        if (!ReservationsMatch(orderItems, reservations, now))
        {
            throw new ApplicationConflictException();
        }
    }

    public async Task EnsureInstallmentReservationAsync(
        Guid orderId,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        if (orderId == Guid.Empty ||
            expiresAt == default)
        {
            throw new ArgumentException(
                "A valid installment order and reservation deadline are required.");
        }

        var now = timeProvider.GetUtcNow();
        if (expiresAt <= now)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expiresAt));
        }

        var orderItems = await dbContext.OrderItems
            .AsNoTracking()
            .Where(item =>
                item.OrderId == orderId &&
                item.PriceCalculationSnapshotId != null)
            .OrderBy(item => item.Id)
            .ToListAsync(cancellationToken);

        var reservations = await dbContext.StockReservations
            .Where(reservation =>
                reservation.OrderId == orderId &&
                reservation.OrderItemId != null)
            .OrderBy(reservation =>
                reservation.OrderItemId)
            .ToListAsync(cancellationToken);

        if (orderItems.Count == 0 ||
            reservations.Count != orderItems.Count ||
            reservations
                .Select(item => item.OrderItemId!.Value)
                .Distinct()
                .Count() != reservations.Count)
        {
            throw new ApplicationConflictException();
        }

        var itemsByOrderItemId =
            orderItems.ToDictionary(item => item.Id);

        var inventoryItemIds = reservations
            .Select(item => item.InventoryItemId)
            .Distinct()
            .ToArray();

        var inventoryItems = await dbContext.InventoryItems
            .Where(item =>
                inventoryItemIds.Contains(item.Id))
            .ToDictionaryAsync(
                item => item.Id,
                cancellationToken);

        var unitIds = reservations
            .Where(item =>
                item.InventoryUnitId != null)
            .Select(item =>
                item.InventoryUnitId!.Value)
            .Distinct()
            .ToArray();

        var units = await dbContext.InventoryUnits
            .Where(item =>
                unitIds.Contains(item.Id))
            .ToDictionaryAsync(
                item => item.Id,
                cancellationToken);

        foreach (var reservation in reservations)
        {
            if (!itemsByOrderItemId.TryGetValue(
                    reservation.OrderItemId!.Value,
                    out var orderItem) ||
                orderItem.InventoryItemId !=
                    reservation.InventoryItemId ||
                orderItem.InventoryUnitId !=
                    reservation.InventoryUnitId ||
                orderItem.Quantity !=
                    reservation.Quantity ||
                !inventoryItems.TryGetValue(
                    reservation.InventoryItemId,
                    out var inventoryItem))
            {
                throw new ApplicationConflictException();
            }

            InventoryUnit? unit = null;

            if (reservation.InventoryUnitId is not null)
            {
                if (!units.TryGetValue(
                        reservation.InventoryUnitId.Value,
                        out unit) ||
                    unit.InventoryItemId !=
                        inventoryItem.Id)
                {
                    throw new ApplicationConflictException();
                }
            }

            if (reservation.Status ==
                StockReservationStatus.Active)
            {
                if (reservation.ExpiresAt < expiresAt)
                {
                    reservation.Extend(expiresAt);
                }

                continue;
            }

            if (reservation.Status !=
                StockReservationStatus.Expired)
            {
                throw new ApplicationConflictException();
            }

            if (reservation.Quantity >
                inventoryItem.QuantityAvailable)
            {
                throw new ApplicationConflictException();
            }

            if (unit is not null &&
                unit.Status != InventoryUnitStatus.Available)
            {
                throw new ApplicationConflictException();
            }

            inventoryItem.Reserve(
                reservation.Quantity);
            unit?.Reserve();
            reservation.Reactivate(expiresAt);

            var movement = CreateMovement(
                inventoryItem,
                StockMovementType.Reservation,
                quantityDelta: 0,
                reservedQuantityDelta:
                    reservation.Quantity,
                unit?.Id,
                "OrderItem",
                orderItem.Id,
                "Installment reservation restored");

            dbContext.StockMovements.Add(movement);

            outboxWriter.AddInventoryChanged(
                inventoryItem,
                movement);
        }

        await PersistenceUtilities.SaveChangesAsync(
            dbContext,
            cancellationToken);

        await EnsurePayableAsync(
            orderId,
            cancellationToken);
    }
    public async Task ConfirmForPaymentAsync(
        Guid orderId,
        Guid paymentId,
        DateTimeOffset confirmedAt,
        CancellationToken cancellationToken)
    {
        var reservations = await dbContext.StockReservations
            .Where(reservation => reservation.OrderId == orderId && reservation.OrderItemId != null)
            .OrderBy(reservation => reservation.OrderItemId)
            .ToListAsync(cancellationToken);
        var orderItems = await dbContext.OrderItems
            .AsNoTracking()
            .Where(item => item.OrderId == orderId && item.PriceCalculationSnapshotId != null)
            .ToListAsync(cancellationToken);
        if (!ReservationsMatch(orderItems, reservations, confirmedAt))
        {
            throw new ApplicationConflictException();
        }

        var itemIds = reservations.Select(reservation => reservation.InventoryItemId).Distinct().ToArray();
        var items = await dbContext.InventoryItems
            .Where(item => itemIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var unitIds = reservations
            .Where(reservation => reservation.InventoryUnitId is not null)
            .Select(reservation => reservation.InventoryUnitId!.Value)
            .Distinct()
            .ToArray();
        var units = await dbContext.InventoryUnits
            .Where(unit => unitIds.Contains(unit.Id))
            .ToDictionaryAsync(unit => unit.Id, cancellationToken);

        foreach (var reservation in reservations)
        {
            if (reservation.Status != StockReservationStatus.Active || reservation.ExpiresAt <= confirmedAt)
            {
                throw new ApplicationConflictException();
            }

            if (!items.TryGetValue(reservation.InventoryItemId, out var item))
            {
                throw new InvalidOperationException("A reservation is missing its inventory item.");
            }

            InventoryUnit? unit = null;
            if (reservation.InventoryUnitId is not null &&
                !units.TryGetValue(reservation.InventoryUnitId.Value, out unit))
            {
                throw new InvalidOperationException("A reservation is missing its inventory unit.");
            }

            if (unit is not null && unit.InventoryItemId != item.Id)
            {
                throw new ApplicationConflictException();
            }

            reservation.Confirm(confirmedAt);
            item.ConfirmReservation(reservation.Quantity);
            unit?.Sell(confirmedAt);
            var movement = CreateMovement(
                item,
                StockMovementType.ReservationConfirmed,
                -reservation.Quantity,
                -reservation.Quantity,
                unit?.Id,
                "Payment",
                paymentId,
                reason: null);
            dbContext.StockMovements.Add(movement);
            outboxWriter.AddInventoryChanged(item, movement);
        }
    }

    public async Task ReleaseForCancellationAsync(
        Guid orderId,
        string reason,
        CancellationToken cancellationToken)
    {
        var releasedAt = timeProvider.GetUtcNow();
        var reservations = await dbContext.StockReservations
            .Where(reservation =>
                reservation.OrderId == orderId &&
                reservation.Status == StockReservationStatus.Active)
            .ToListAsync(cancellationToken);
        if (reservations.Count == 0)
        {
            return;
        }

        var itemIds = reservations.Select(reservation => reservation.InventoryItemId).Distinct().ToArray();
        var items = await dbContext.InventoryItems
            .Where(item => itemIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var unitIds = reservations
            .Where(reservation => reservation.InventoryUnitId is not null)
            .Select(reservation => reservation.InventoryUnitId!.Value)
            .Distinct()
            .ToArray();
        var units = await dbContext.InventoryUnits
            .Where(unit => unitIds.Contains(unit.Id))
            .ToDictionaryAsync(unit => unit.Id, cancellationToken);

        foreach (var reservation in reservations)
        {
            if (!items.TryGetValue(reservation.InventoryItemId, out var item))
            {
                throw new InvalidOperationException("A reservation is missing its inventory item.");
            }

            InventoryUnit? unit = null;
            if (reservation.InventoryUnitId is not null &&
                !units.TryGetValue(reservation.InventoryUnitId.Value, out unit))
            {
                throw new InvalidOperationException("A reservation is missing its inventory unit.");
            }

            if (unit is not null && unit.InventoryItemId != item.Id)
            {
                throw new ApplicationConflictException();
            }

            reservation.Release(releasedAt);
            item.ReleaseReservation(reservation.Quantity);
            unit?.ReleaseReservation();
            var movement = CreateMovement(
                item,
                StockMovementType.ReservationReleased,
                quantityDelta: 0,
                reservedQuantityDelta: -reservation.Quantity,
                unit?.Id,
                "OrderCancellation",
                orderId,
                reason);
            dbContext.StockMovements.Add(movement);
            outboxWriter.AddInventoryChanged(item, movement);
        }
    }

    private StockMovement CreateMovement(
        InventoryItem item,
        StockMovementType movementType,
        int quantityDelta,
        int reservedQuantityDelta,
        Guid? inventoryUnitId,
        string referenceType,
        Guid referenceId,
        string? reason)
    {
        var movement = new StockMovement(
            item.Id,
            movementType,
            quantityDelta,
            item.QuantityOnHand,
            timeProvider.GetUtcNow(),
            reservedQuantityDelta,
            item.QuantityReserved,
            inventoryUnitId);
        movement.SetReference(referenceType, referenceId, reason);
        return movement;
    }

    private static bool ReservationsMatch(
        IReadOnlyList<OrderItem> orderItems,
        IReadOnlyList<StockReservation> reservations,
        DateTimeOffset at)
    {
        if (orderItems.Count == 0 || reservations.Count != orderItems.Count)
        {
            return false;
        }

        if (reservations.Any(reservation => reservation.OrderItemId is null) ||
            reservations.Select(reservation => reservation.OrderItemId!.Value).Distinct().Count() !=
            reservations.Count)
        {
            return false;
        }

        var itemsById = orderItems.ToDictionary(item => item.Id);
        return reservations.All(reservation =>
            itemsById.TryGetValue(reservation.OrderItemId!.Value, out var item) &&
            item.InventoryItemId == reservation.InventoryItemId &&
            item.InventoryUnitId == reservation.InventoryUnitId &&
            item.Quantity == reservation.Quantity &&
            reservation.Status == StockReservationStatus.Active &&
            reservation.ExpiresAt > at);
    }
}
