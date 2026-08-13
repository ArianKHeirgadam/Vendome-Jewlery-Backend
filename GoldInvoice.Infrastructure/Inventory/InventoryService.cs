using GoldInvoice.Application.Common;
using GoldInvoice.Application.Integration;
using GoldInvoice.Application.Inventory;
using GoldInvoice.Domain.Inventory;
using GoldInvoice.Infrastructure.Integration;
using GoldInvoice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoldInvoice.Infrastructure.Inventory;

internal sealed class InventoryService(
    GoldInvoiceDbContext dbContext,
    IOutboxWriter outboxWriter,
    TimeProvider timeProvider) : IInventoryService
{
    private const int MaximumPageSize = 100;
    private const int MaximumReservationExpirationBatchSize = 500;

    public async Task<IReadOnlyList<WarehouseInfo>> GetWarehousesAsync(
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Warehouses.AsNoTracking();
        if (!includeInactive)
        {
            query = query.Where(warehouse => warehouse.IsActive);
        }

        return (await query
                .OrderBy(warehouse => warehouse.Code)
                .ToListAsync(cancellationToken))
            .Select(MapWarehouse)
            .ToArray();
    }

    public async Task<WarehouseInfo> GetWarehouseAsync(
        Guid warehouseId,
        CancellationToken cancellationToken)
    {
        var warehouse = await dbContext.Warehouses
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == warehouseId, cancellationToken) ??
            throw new ApplicationResourceNotFoundException();
        return MapWarehouse(warehouse);
    }

    public async Task<WarehouseInfo> CreateWarehouseAsync(
        CreateWarehouseCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var warehouse = new Warehouse(command.Code, command.Name);
        dbContext.Warehouses.Add(warehouse);
        await SaveChangesAsync(cancellationToken);
        return MapWarehouse(warehouse);
    }

    public async Task<WarehouseInfo> UpdateWarehouseAsync(
        Guid warehouseId,
        UpdateWarehouseCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var warehouse = await dbContext.Warehouses.FindAsync([warehouseId], cancellationToken) ??
            throw new ApplicationResourceNotFoundException();
        SetOriginalRowVersion(warehouse, command.RowVersion);
        warehouse.Update(command.Code, command.Name, command.IsActive);
        await SaveChangesAsync(cancellationToken);
        return MapWarehouse(warehouse);
    }

    public async Task<InventoryItemInfo> GetInventoryItemAsync(
        Guid inventoryItemId,
        CancellationToken cancellationToken)
    {
        var item = await dbContext.InventoryItems
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == inventoryItemId, cancellationToken) ??
            throw new ApplicationResourceNotFoundException();
        return MapInventoryItem(item);
    }

    public async Task<PagedResult<InventoryItemInfo>> GetInventoryItemsAsync(
        Guid? warehouseId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        ValidatePagination(page, pageSize);
        var query = dbContext.InventoryItems.AsNoTracking();
        if (warehouseId is not null)
        {
            query = query.Where(item => item.WarehouseId == warehouseId);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(item => item.WarehouseId)
            .ThenBy(item => item.ProductVariantId)
            .ThenBy(item => item.Id)
            .Skip(CalculateSkip(page, pageSize))
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<InventoryItemInfo>(
            items.Select(MapInventoryItem).ToArray(),
            page,
            pageSize,
            totalCount);
    }

    public async Task<InventoryUnitInfo> GetInventoryUnitAsync(
        Guid inventoryUnitId,
        CancellationToken cancellationToken)
    {
        var unit = await dbContext.InventoryUnits
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == inventoryUnitId, cancellationToken) ??
            throw new ApplicationResourceNotFoundException();
        return MapInventoryUnit(unit);
    }

    public async Task<InventoryUnitInfo> FindInventoryUnitAsync(
        string serialNumberOrBarcode,
        CancellationToken cancellationToken)
    {
        var normalizedIdentifier = string.IsNullOrWhiteSpace(serialNumberOrBarcode)
            ? throw new ArgumentException("A serial number or barcode is required.", nameof(serialNumberOrBarcode))
            : serialNumberOrBarcode.Trim().ToUpperInvariant();
        if (normalizedIdentifier.Length > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(serialNumberOrBarcode));
        }

        var matches = await dbContext.InventoryUnits
            .AsNoTracking()
            .Where(unit => unit.SerialNumber == normalizedIdentifier || unit.Barcode == normalizedIdentifier)
            .OrderBy(unit => unit.Id)
            .Take(2)
            .ToListAsync(cancellationToken);
        return matches.Count switch
        {
            0 => throw new ApplicationResourceNotFoundException(),
            1 => MapInventoryUnit(matches[0]),
            _ => throw new ApplicationConflictException()
        };
    }

    public async Task<PagedResult<StockMovementInfo>> GetStockMovementsAsync(
        Guid inventoryItemId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        ValidatePagination(page, pageSize);
        if (!await dbContext.InventoryItems.AnyAsync(item => item.Id == inventoryItemId, cancellationToken))
        {
            throw new ApplicationResourceNotFoundException();
        }

        var query = dbContext.StockMovements
            .AsNoTracking()
            .Where(movement => movement.InventoryItemId == inventoryItemId);
        var totalCount = await query.CountAsync(cancellationToken);
        var movements = await query
            .OrderByDescending(movement => movement.OccurredAt)
            .ThenByDescending(movement => movement.Id)
            .Skip(CalculateSkip(page, pageSize))
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<StockMovementInfo>(
            movements.Select(MapMovement).ToArray(),
            page,
            pageSize,
            totalCount);
    }

    public async Task<InventoryItemInfo> ReceiveStockAsync(
        ReceiveStockCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        await EnsureWarehouseAndVariantExistAsync(
            command.WarehouseId,
            command.ProductVariantId,
            cancellationToken);
        await using var transaction = await BeginTransactionAsync(cancellationToken);
        var item = await GetOrCreateInventoryItemAsync(
            command.WarehouseId,
            command.ProductVariantId,
            cancellationToken);
        item.Receive(command.Quantity);
        var movement = CreateMovement(
            item,
            StockMovementType.Purchase,
            command.Quantity,
            reservedQuantityDelta: 0,
            inventoryUnitId: null,
            command.ReferenceType,
            command.ReferenceId,
            command.Reason);
        dbContext.StockMovements.Add(movement);
        outboxWriter.AddInventoryChanged(item, movement);
        await SaveChangesAsync(cancellationToken);
        await CommitAsync(transaction, cancellationToken);
        return MapInventoryItem(item);
    }

    public async Task<InventoryItemInfo> AdjustStockAsync(
        AdjustStockCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        await using var transaction = await BeginTransactionAsync(cancellationToken);
        var item = await dbContext.InventoryItems.FindAsync([command.InventoryItemId], cancellationToken) ??
            throw new ApplicationResourceNotFoundException();
        SetOriginalRowVersion(item, command.RowVersion);
        item.Adjust(command.QuantityDelta);
        var movement = CreateMovement(
            item,
            StockMovementType.ManualAdjustment,
            command.QuantityDelta,
            reservedQuantityDelta: 0,
            inventoryUnitId: null,
            referenceType: "InventoryAdjustment",
            referenceId: null,
            command.Reason);
        var adjustment = new InventoryAdjustment(
            item.Id,
            movement.Id,
            command.QuantityDelta,
            command.Reason);
        movement.SetReference("InventoryAdjustment", adjustment.Id, command.Reason);
        dbContext.StockMovements.Add(movement);
        dbContext.InventoryAdjustments.Add(adjustment);
        outboxWriter.AddInventoryChanged(item, movement);
        await SaveChangesAsync(cancellationToken);
        await CommitAsync(transaction, cancellationToken);
        return MapInventoryItem(item);
    }

    public async Task<InventoryUnitInfo> ReceiveInventoryUnitAsync(
        ReceiveInventoryUnitCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var goldDefinition = await (
                from variant in dbContext.ProductVariants
                join product in dbContext.Products
                    on variant.ProductId equals product.Id
                join detail in dbContext.GoldProductDetails
                    on variant.Id equals detail.ProductVariantId
                where variant.Id == command.ProductVariantId &&
                    variant.ProductId == command.ProductId &&
                    variant.IsActive &&
                    product.IsActive
                select new
                {
                    detail.Karat,
                    detail.GrossWeight,
                    detail.NetGoldWeight,
                    detail.IsWeightVariable
                })
            .SingleOrDefaultAsync(cancellationToken);
        var validWarehouse = await dbContext.Warehouses.AnyAsync(
            warehouse => warehouse.Id == command.WarehouseId && warehouse.IsActive,
            cancellationToken);
        if (goldDefinition is null || !validWarehouse)
        {
            throw new ApplicationResourceNotFoundException();
        }

        if (goldDefinition.Karat != command.Karat ||
            (!goldDefinition.IsWeightVariable &&
             (goldDefinition.GrossWeight != command.ActualGrossWeight ||
              goldDefinition.NetGoldWeight != command.ActualNetGoldWeight)))
        {
            throw new ApplicationConflictException();
        }

        await using var transaction = await BeginTransactionAsync(cancellationToken);
        var item = await GetOrCreateInventoryItemAsync(
            command.WarehouseId,
            command.ProductVariantId,
            cancellationToken);
        var now = timeProvider.GetUtcNow();
        var receivedAt = (command.ReceivedAt ?? now).ToUniversalTime();
        if (receivedAt > now)
        {
            throw new ArgumentOutOfRangeException(nameof(command.ReceivedAt));
        }
        var unit = new InventoryUnit(
            command.ProductId,
            command.ProductVariantId,
            command.WarehouseId,
            item.Id,
            command.SerialNumber,
            command.Barcode,
            command.ActualGrossWeight,
            command.ActualNetGoldWeight,
            command.Karat,
            command.AcquisitionCostRials,
            receivedAt);
        item.ReceivePurchase(1, command.AcquisitionCostRials);
        var movement = CreateMovement(
            item,
            StockMovementType.Purchase,
            quantityDelta: 1,
            reservedQuantityDelta: 0,
            unit.Id,
            referenceType: "InventoryUnitReceipt",
            referenceId: unit.Id,
            reason: null);
        dbContext.InventoryUnits.Add(unit);
        dbContext.StockMovements.Add(movement);
        outboxWriter.AddInventoryChanged(item, movement);
        await SaveChangesAsync(cancellationToken);
        await CommitAsync(transaction, cancellationToken);
        return MapInventoryUnit(unit);
    }

    public async Task<StockReservationInfo> ReserveAsync(
        ReserveStockCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.LifetimeMinutes is < 1 or > 1440)
        {
            throw new ArgumentOutOfRangeException(nameof(command.LifetimeMinutes));
        }

        if (command.InventoryUnitId == Guid.Empty)
        {
            throw new ArgumentException("A non-empty inventory-unit identifier is required.", nameof(command));
        }

        if (command.InventoryUnitId is not null && command.Quantity != 1)
        {
            throw new ArgumentOutOfRangeException(nameof(command.Quantity));
        }

        if (string.IsNullOrWhiteSpace(command.ReservationKey) || command.ReservationKey.Trim().Length > 128)
        {
            throw new ArgumentException("A valid reservation key is required.", nameof(command.ReservationKey));
        }

        var orderExists = await dbContext.Orders.AnyAsync(order => order.Id == command.OrderId, cancellationToken);
        if (!orderExists)
        {
            throw new ApplicationResourceNotFoundException();
        }

        await using var transaction = await BeginTransactionAsync(cancellationToken);
        var item = await dbContext.InventoryItems.FindAsync([command.InventoryItemId], cancellationToken) ??
            throw new ApplicationResourceNotFoundException();
        SetOriginalRowVersion(item, command.InventoryRowVersion);
        InventoryUnit? unit = null;
        if (command.InventoryUnitId is not null)
        {
            unit = await dbContext.InventoryUnits.FindAsync([command.InventoryUnitId.Value], cancellationToken) ??
                throw new ApplicationResourceNotFoundException();
            if (unit.InventoryItemId != item.Id)
            {
                throw new ApplicationConflictException();
            }

            unit.Reserve();
        }

        item.Reserve(command.Quantity);
        var now = timeProvider.GetUtcNow();
        var reservation = new StockReservation(
            item.Id,
            command.OrderId,
            command.ReservationKey,
            command.Quantity,
            now.AddMinutes(command.LifetimeMinutes),
            unit?.Id);
        var movement = CreateMovement(
            item,
            StockMovementType.Reservation,
            quantityDelta: 0,
            reservedQuantityDelta: command.Quantity,
            unit?.Id,
            referenceType: "StockReservation",
            referenceId: reservation.Id,
            reason: null);
        dbContext.StockReservations.Add(reservation);
        dbContext.StockMovements.Add(movement);
        outboxWriter.AddInventoryChanged(item, movement);
        await SaveChangesAsync(cancellationToken);
        await CommitAsync(transaction, cancellationToken);
        return MapReservation(reservation);
    }

    public Task<StockReservationInfo> ReleaseReservationAsync(
        Guid reservationId,
        string reservationRowVersion,
        string inventoryRowVersion,
        CancellationToken cancellationToken) =>
        ChangeReservationAsync(
            reservationId,
            reservationRowVersion,
            inventoryRowVersion,
            confirm: false,
            cancellationToken);

    public Task<StockReservationInfo> ConfirmReservationAsync(
        Guid reservationId,
        string reservationRowVersion,
        string inventoryRowVersion,
        CancellationToken cancellationToken) =>
        ChangeReservationAsync(
            reservationId,
            reservationRowVersion,
            inventoryRowVersion,
            confirm: true,
            cancellationToken);

    public async Task TransferStockAsync(
        TransferStockCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(command.Quantity));
        }

        await using var transaction = await BeginTransactionAsync(cancellationToken);
        var source = await dbContext.InventoryItems.FindAsync([command.SourceInventoryItemId], cancellationToken) ??
            throw new ApplicationResourceNotFoundException();
        SetOriginalRowVersion(source, command.SourceRowVersion);
        if (source.WarehouseId == command.DestinationWarehouseId)
        {
            throw new ApplicationConflictException();
        }

        var destinationExists = await dbContext.Warehouses.AnyAsync(
            warehouse => warehouse.Id == command.DestinationWarehouseId && warehouse.IsActive,
            cancellationToken);
        var hasTrackedUnits = await dbContext.InventoryUnits.AnyAsync(
            unit => unit.InventoryItemId == source.Id &&
                unit.Status != InventoryUnitStatus.Inactive &&
                unit.Status != InventoryUnitStatus.Sold,
            cancellationToken);
        if (!destinationExists)
        {
            throw new ApplicationResourceNotFoundException();
        }

        if (hasTrackedUnits)
        {
            throw new ApplicationConflictException();
        }

        var destination = await GetOrCreateInventoryItemAsync(
            command.DestinationWarehouseId,
            source.ProductVariantId,
            cancellationToken);
        var transferCost = source.HasAcquisitionCost
            ? source.AverageUnitCostRials
            : (long?)null;
        source.Adjust(-command.Quantity);
        if (transferCost is null)
        {
            destination.Receive(command.Quantity);
        }
        else
        {
            destination.ReceivePurchase(command.Quantity, transferCost.Value);
        }
        var transferId = Guid.NewGuid();
        var sourceMovement = CreateMovement(
                source,
                StockMovementType.TransferOut,
                -command.Quantity,
                0,
                null,
                "InventoryTransfer",
                transferId,
                null);
        var destinationMovement = CreateMovement(
                destination,
                StockMovementType.TransferIn,
                command.Quantity,
                0,
                null,
                "InventoryTransfer",
                transferId,
                null);
        dbContext.StockMovements.AddRange(sourceMovement, destinationMovement);
        outboxWriter.AddInventoryChanged(source, sourceMovement);
        outboxWriter.AddInventoryChanged(destination, destinationMovement);
        await SaveChangesAsync(cancellationToken);
        await CommitAsync(transaction, cancellationToken);
    }

    public async Task<InventoryUnitInfo> TransferInventoryUnitAsync(
        TransferInventoryUnitCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        await using var transaction = await BeginTransactionAsync(cancellationToken);
        var unit = await dbContext.InventoryUnits.FindAsync([command.InventoryUnitId], cancellationToken) ??
            throw new ApplicationResourceNotFoundException();
        var source = await dbContext.InventoryItems.FindAsync([unit.InventoryItemId], cancellationToken) ??
            throw new ApplicationResourceNotFoundException();
        SetOriginalRowVersion(unit, command.UnitRowVersion);
        SetOriginalRowVersion(source, command.SourceInventoryRowVersion);
        if (unit.WarehouseId == command.DestinationWarehouseId)
        {
            throw new ApplicationConflictException();
        }

        var destinationExists = await dbContext.Warehouses.AnyAsync(
            warehouse => warehouse.Id == command.DestinationWarehouseId && warehouse.IsActive,
            cancellationToken);
        if (!destinationExists)
        {
            throw new ApplicationResourceNotFoundException();
        }

        var destination = await GetOrCreateInventoryItemAsync(
            command.DestinationWarehouseId,
            unit.ProductVariantId,
            cancellationToken);
        source.Adjust(-1);
        destination.ReceivePurchase(1, unit.AcquisitionCostRials);
        unit.TransferTo(destination.WarehouseId, destination.Id);
        var transferId = Guid.NewGuid();
        var sourceMovement = CreateMovement(
                source,
                StockMovementType.TransferOut,
                -1,
                0,
                unit.Id,
                "InventoryUnitTransfer",
                transferId,
                null);
        var destinationMovement = CreateMovement(
                destination,
                StockMovementType.TransferIn,
                1,
                0,
                unit.Id,
                "InventoryUnitTransfer",
                transferId,
                null);
        dbContext.StockMovements.AddRange(sourceMovement, destinationMovement);
        outboxWriter.AddInventoryChanged(source, sourceMovement);
        outboxWriter.AddInventoryChanged(destination, destinationMovement);
        await SaveChangesAsync(cancellationToken);
        await CommitAsync(transaction, cancellationToken);
        return MapInventoryUnit(unit);
    }

    public async Task<int> ExpireReservationsAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        await using var transaction = await BeginTransactionAsync(cancellationToken);
        var reservations = await dbContext.StockReservations
            .Where(reservation =>
                reservation.Status == StockReservationStatus.Active &&
                reservation.ExpiresAt <= now)
            .OrderBy(reservation => reservation.ExpiresAt)
            .Take(MaximumReservationExpirationBatchSize)
            .ToListAsync(cancellationToken);
        if (reservations.Count == 0)
        {
            await CommitAsync(transaction, cancellationToken);
            return 0;
        }

        var inventoryItemIds = reservations.Select(reservation => reservation.InventoryItemId).Distinct().ToArray();
        var inventoryItems = await dbContext.InventoryItems
            .Where(item => inventoryItemIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var inventoryUnitIds = reservations
            .Where(reservation => reservation.InventoryUnitId is not null)
            .Select(reservation => reservation.InventoryUnitId!.Value)
            .Distinct()
            .ToArray();
        var inventoryUnits = await dbContext.InventoryUnits
            .Where(unit => inventoryUnitIds.Contains(unit.Id))
            .ToDictionaryAsync(unit => unit.Id, cancellationToken);

        foreach (var reservation in reservations)
        {
            if (!inventoryItems.TryGetValue(reservation.InventoryItemId, out var item))
            {
                throw new InvalidOperationException("A reservation is missing its inventory item.");
            }

            reservation.Expire(now);
            item.ReleaseReservation(reservation.Quantity);
            InventoryUnit? unit = null;
            if (reservation.InventoryUnitId is not null)
            {
                if (!inventoryUnits.TryGetValue(reservation.InventoryUnitId.Value, out unit))
                {
                    throw new InvalidOperationException("A reservation is missing its inventory unit.");
                }

                unit.ReleaseReservation();
            }

            var movement = CreateMovement(
                item,
                StockMovementType.ReservationReleased,
                quantityDelta: 0,
                reservedQuantityDelta: -reservation.Quantity,
                unit?.Id,
                referenceType: "StockReservationExpiration",
                referenceId: reservation.Id,
                reason: null);
            dbContext.StockMovements.Add(movement);
            outboxWriter.AddInventoryChanged(item, movement);
        }

        await SaveChangesAsync(cancellationToken);
        await CommitAsync(transaction, cancellationToken);
        return reservations.Count;
    }

    private async Task<StockReservationInfo> ChangeReservationAsync(
        Guid reservationId,
        string reservationRowVersion,
        string inventoryRowVersion,
        bool confirm,
        CancellationToken cancellationToken)
    {
        await using var transaction = await BeginTransactionAsync(cancellationToken);
        var reservation = await dbContext.StockReservations.FindAsync([reservationId], cancellationToken) ??
            throw new ApplicationResourceNotFoundException();
        if (reservation.OrderItemId is not null)
        {
            throw new ApplicationConflictException();
        }

        var item = await dbContext.InventoryItems.FindAsync([reservation.InventoryItemId], cancellationToken) ??
            throw new ApplicationResourceNotFoundException();
        SetOriginalRowVersion(reservation, reservationRowVersion);
        SetOriginalRowVersion(item, inventoryRowVersion);
        InventoryUnit? unit = null;
        if (reservation.InventoryUnitId is not null)
        {
            unit = await dbContext.InventoryUnits.FindAsync([reservation.InventoryUnitId.Value], cancellationToken) ??
                throw new ApplicationResourceNotFoundException();
        }

        var now = timeProvider.GetUtcNow();
        if (confirm)
        {
            reservation.Confirm(now);
            item.ConfirmReservation(reservation.Quantity);
            unit?.Sell(now);
        }
        else
        {
            reservation.Release(now);
            item.ReleaseReservation(reservation.Quantity);
            unit?.ReleaseReservation();
        }

        var movement = CreateMovement(
            item,
            confirm ? StockMovementType.ReservationConfirmed : StockMovementType.ReservationReleased,
            quantityDelta: confirm ? -reservation.Quantity : 0,
            reservedQuantityDelta: -reservation.Quantity,
            unit?.Id,
            referenceType: "StockReservation",
            referenceId: reservation.Id,
            reason: null);
        dbContext.StockMovements.Add(movement);
        outboxWriter.AddInventoryChanged(item, movement);
        await SaveChangesAsync(cancellationToken);
        await CommitAsync(transaction, cancellationToken);
        return MapReservation(reservation);
    }

    private async Task EnsureWarehouseAndVariantExistAsync(
        Guid warehouseId,
        Guid productVariantId,
        CancellationToken cancellationToken)
    {
        var warehouseExists = await dbContext.Warehouses.AnyAsync(
            warehouse => warehouse.Id == warehouseId && warehouse.IsActive,
            cancellationToken);
        var variantExists = await (
                from variant in dbContext.ProductVariants
                join product in dbContext.Products
                    on variant.ProductId equals product.Id
                where variant.Id == productVariantId && variant.IsActive && product.IsActive
                select variant.Id)
            .AnyAsync(cancellationToken);
        if (!warehouseExists || !variantExists)
        {
            throw new ApplicationResourceNotFoundException();
        }
    }

    private async Task<InventoryItem> GetOrCreateInventoryItemAsync(
        Guid warehouseId,
        Guid productVariantId,
        CancellationToken cancellationToken)
    {
        var item = await dbContext.InventoryItems.SingleOrDefaultAsync(
            candidate => candidate.WarehouseId == warehouseId &&
                candidate.ProductVariantId == productVariantId,
            cancellationToken);
        if (item is not null)
        {
            return item;
        }

        item = new InventoryItem(warehouseId, productVariantId);
        dbContext.InventoryItems.Add(item);
        return item;
    }

    private StockMovement CreateMovement(
        InventoryItem item,
        StockMovementType movementType,
        int quantityDelta,
        int reservedQuantityDelta,
        Guid? inventoryUnitId,
        string? referenceType,
        Guid? referenceId,
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

    private void SetOriginalRowVersion<TEntity>(TEntity entity, string value)
        where TEntity : class =>
        dbContext.Entry(entity).Property("RowVersion").OriginalValue = DecodeRowVersion(value);

    private async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ApplicationConcurrencyException();
        }
        catch (DbUpdateException)
        {
            throw new ApplicationConflictException();
        }
    }

    private async Task<IDbContextTransaction?> BeginTransactionAsync(CancellationToken cancellationToken) =>
        dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

    private static async Task CommitAsync(
        IDbContextTransaction? transaction,
        CancellationToken cancellationToken)
    {
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
    }

    private static InventoryItemInfo MapInventoryItem(InventoryItem item) => new(
        item.Id,
        item.WarehouseId,
        item.ProductVariantId,
        item.QuantityOnHand,
        item.QuantityReserved,
        item.QuantityAvailable,
        item.AverageUnitCostRials,
        item.HasAcquisitionCost,
        Convert.ToBase64String(item.RowVersion));

    private static WarehouseInfo MapWarehouse(Warehouse warehouse) => new(
        warehouse.Id,
        warehouse.Code,
        warehouse.Name,
        warehouse.IsActive,
        Convert.ToBase64String(warehouse.RowVersion));

    private static InventoryUnitInfo MapInventoryUnit(InventoryUnit unit) => new(
        unit.Id,
        unit.ProductId,
        unit.ProductVariantId,
        unit.WarehouseId,
        unit.InventoryItemId,
        unit.SerialNumber,
        unit.Barcode,
        unit.ActualGrossWeight,
        unit.ActualNetGoldWeight,
        unit.Karat,
        unit.Status,
        unit.ReceivedAt,
        unit.SoldAt,
        Convert.ToBase64String(unit.RowVersion));

    private static StockReservationInfo MapReservation(StockReservation reservation) => new(
        reservation.Id,
        reservation.InventoryItemId,
        reservation.InventoryUnitId,
        reservation.OrderId,
        reservation.ReservationKey,
        reservation.Quantity,
        reservation.Status,
        reservation.ExpiresAt,
        Convert.ToBase64String(reservation.RowVersion));

    private static StockMovementInfo MapMovement(StockMovement movement) => new(
        movement.Id,
        movement.InventoryItemId,
        movement.InventoryUnitId,
        movement.MovementType,
        movement.QuantityDelta,
        movement.BalanceAfter,
        movement.ReservedQuantityDelta,
        movement.ReservedBalanceAfter,
        movement.ReferenceType,
        movement.ReferenceId,
        movement.Reason,
        movement.OccurredAt);

    private static void ValidatePagination(int page, int pageSize)
    {
        if (page < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(page));
        }

        if (pageSize is < 1 or > MaximumPageSize)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        }
    }

    private static int CalculateSkip(int page, int pageSize)
    {
        var skip = (long)(page - 1) * pageSize;
        return skip <= int.MaxValue
            ? (int)skip
            : throw new ArgumentOutOfRangeException(nameof(page));
    }

    private static byte[] DecodeRowVersion(string value)
    {
        try
        {
            return Convert.FromBase64String(value ?? string.Empty);
        }
        catch (FormatException exception)
        {
            throw new ArgumentException("The concurrency token is invalid.", nameof(value), exception);
        }
    }
}
