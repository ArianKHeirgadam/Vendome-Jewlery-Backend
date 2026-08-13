using GoldInvoice.Application.Inventory;
using GoldInvoice.Application.Security;
using GoldInvoice.Contracts.Common;
using GoldInvoice.Contracts.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GoldInvoice.Api.Controllers;

[ApiController]
[Authorize]
[RequestSizeLimit(32 * 1024)]
[Route("api/v1/inventory")]
public sealed class InventoryController(
    IInventoryService inventoryService,
    ISupplierPurchaseService supplierPurchaseService) : ControllerBase
{
    [Authorize(Policy = SecurityPermissions.InventoryRead)]
    [HttpGet("warehouses")]
    public async Task<ActionResult<IReadOnlyList<WarehouseResponse>>> GetWarehouses(
        [FromQuery] bool includeInactive,
        CancellationToken cancellationToken) =>
        Ok((await inventoryService.GetWarehousesAsync(includeInactive, cancellationToken))
            .Select(MapWarehouse)
            .ToArray());

    [Authorize(Policy = SecurityPermissions.InventoryRead)]
    [HttpGet("warehouses/{warehouseId:guid}")]
    public async Task<ActionResult<WarehouseResponse>> GetWarehouse(
        Guid warehouseId,
        CancellationToken cancellationToken) =>
        Ok(MapWarehouse(await inventoryService.GetWarehouseAsync(warehouseId, cancellationToken)));

    [Authorize(Policy = SecurityPermissions.InventoryAdjust)]
    [HttpPost("warehouses")]
    public async Task<ActionResult<WarehouseResponse>> CreateWarehouse(
        CreateWarehouseRequest request,
        CancellationToken cancellationToken)
    {
        var warehouse = await inventoryService.CreateWarehouseAsync(
            new CreateWarehouseCommand(request.Code, request.Name),
            cancellationToken);
        return CreatedAtAction(
            nameof(GetWarehouse),
            new { warehouseId = warehouse.Id },
            MapWarehouse(warehouse));
    }

    [Authorize(Policy = SecurityPermissions.InventoryAdjust)]
    [HttpPut("warehouses/{warehouseId:guid}")]
    public async Task<ActionResult<WarehouseResponse>> UpdateWarehouse(
        Guid warehouseId,
        UpdateWarehouseRequest request,
        CancellationToken cancellationToken) =>
        Ok(MapWarehouse(await inventoryService.UpdateWarehouseAsync(
            warehouseId,
            new UpdateWarehouseCommand(
                request.Code,
                request.Name,
                request.IsActive,
                request.RowVersion),
            cancellationToken)));

    [Authorize(Policy = SecurityPermissions.InventoryRead)]
    [HttpGet("items")]
    public async Task<ActionResult<PagedResponse<InventoryItemResponse>>> GetInventoryItems(
        [FromQuery] Guid? warehouseId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100,
        CancellationToken cancellationToken = default)
    {
        var result = await inventoryService.GetInventoryItemsAsync(
            warehouseId,
            page,
            pageSize,
            cancellationToken);
        return Ok(new PagedResponse<InventoryItemResponse>
        {
            Items = result.Items.Select(MapItem).ToArray(),
            Page = result.Page,
            PageSize = result.PageSize,
            TotalCount = result.TotalCount
        });
    }

    [Authorize(Policy = SecurityPermissions.InventoryRead)]
    [HttpGet("items/{inventoryItemId:guid}")]
    public async Task<ActionResult<InventoryItemResponse>> GetInventoryItem(
        Guid inventoryItemId,
        CancellationToken cancellationToken) =>
        Ok(MapItem(await inventoryService.GetInventoryItemAsync(inventoryItemId, cancellationToken)));

    [Authorize(Policy = SecurityPermissions.SuppliersRead)]
    [Authorize(Policy = SecurityPermissions.InventoryRead)]
    [HttpGet("supplier-purchases")]
    public async Task<ActionResult<PagedResponse<SupplierPurchaseResponse>>> GetSupplierPurchases(
        [FromQuery] Guid? supplierId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100,
        CancellationToken cancellationToken = default)
    {
        var result = await supplierPurchaseService.GetPurchasesAsync(
            page,
            pageSize,
            supplierId,
            cancellationToken);
        return Ok(new PagedResponse<SupplierPurchaseResponse>
        {
            Items = result.Items.Select(MapPurchase).ToArray(),
            Page = result.Page,
            PageSize = result.PageSize,
            TotalCount = result.TotalCount
        });
    }

    [Authorize(Policy = SecurityPermissions.SuppliersManage)]
    [Authorize(Policy = SecurityPermissions.InventoryAdjust)]
    [HttpPost("supplier-purchases")]
    public async Task<ActionResult<SupplierPurchaseResponse>> RecordSupplierPurchase(
        RecordSupplierPurchaseRequest request,
        CancellationToken cancellationToken)
    {
        var purchase = await supplierPurchaseService.RecordPurchaseAsync(
            new RecordSupplierPurchaseCommand(
                request.SupplierId,
                request.WarehouseId,
                request.ProductVariantId,
                request.Quantity,
                request.UnitCostRials,
                request.SellingUnitPriceRials,
                request.PurchasedAt,
                request.SupplierReference,
                request.Notes),
            cancellationToken);
        return Created($"/api/v1/inventory/supplier-purchases/{purchase.Id:D}", MapPurchase(purchase));
    }

    [Authorize(Policy = SecurityPermissions.InventoryRead)]
    [HttpGet("units/{inventoryUnitId:guid}")]
    public async Task<ActionResult<InventoryUnitResponse>> GetInventoryUnit(
        Guid inventoryUnitId,
        CancellationToken cancellationToken) =>
        Ok(MapUnit(await inventoryService.GetInventoryUnitAsync(inventoryUnitId, cancellationToken)));

    [Authorize(Policy = SecurityPermissions.InventoryRead)]
    [HttpGet("units/lookup")]
    public async Task<ActionResult<InventoryUnitResponse>> FindInventoryUnit(
        [FromQuery] string identifier,
        CancellationToken cancellationToken) =>
        Ok(MapUnit(await inventoryService.FindInventoryUnitAsync(identifier, cancellationToken)));

    [Authorize(Policy = SecurityPermissions.InventoryRead)]
    [HttpGet("items/{inventoryItemId:guid}/movements")]
    public async Task<ActionResult<PagedResponse<StockMovementResponse>>> GetStockMovements(
        Guid inventoryItemId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var result = await inventoryService.GetStockMovementsAsync(
            inventoryItemId,
            page,
            pageSize,
            cancellationToken);
        return Ok(new PagedResponse<StockMovementResponse>
        {
            Items = result.Items.Select(MapMovement).ToArray(),
            Page = result.Page,
            PageSize = result.PageSize,
            TotalCount = result.TotalCount
        });
    }

    [Authorize(Policy = SecurityPermissions.InventoryAdjust)]
    [HttpPost("receipts")]
    public async Task<ActionResult<InventoryItemResponse>> ReceiveStock(
        ReceiveStockRequest request,
        CancellationToken cancellationToken) =>
        Ok(MapItem(await inventoryService.ReceiveStockAsync(
            new ReceiveStockCommand(
                request.WarehouseId,
                request.ProductVariantId,
                request.Quantity,
                request.ReferenceType,
                request.ReferenceId,
                request.Reason),
            cancellationToken)));

    [Authorize(Policy = SecurityPermissions.InventoryAdjust)]
    [HttpPost("items/{inventoryItemId:guid}/adjustments")]
    public async Task<ActionResult<InventoryItemResponse>> AdjustStock(
        Guid inventoryItemId,
        AdjustStockRequest request,
        CancellationToken cancellationToken) =>
        Ok(MapItem(await inventoryService.AdjustStockAsync(
            new AdjustStockCommand(
                inventoryItemId,
                request.QuantityDelta,
                request.Reason,
                request.RowVersion),
            cancellationToken)));

    [Authorize(Policy = SecurityPermissions.InventoryAdjust)]
    [HttpPost("units")]
    public async Task<ActionResult<InventoryUnitResponse>> ReceiveInventoryUnit(
        ReceiveInventoryUnitRequest request,
        CancellationToken cancellationToken)
    {
        var unit = await inventoryService.ReceiveInventoryUnitAsync(
            new ReceiveInventoryUnitCommand(
                request.ProductId,
                request.ProductVariantId,
                request.WarehouseId,
                request.SerialNumber,
                request.Barcode,
                request.ActualGrossWeight,
                request.ActualNetGoldWeight,
                request.Karat,
                request.AcquisitionCostRials,
                request.ReceivedAt),
            cancellationToken);
        return Created($"/api/v1/inventory/units/{unit.Id:D}", MapUnit(unit));
    }

    [Authorize(Policy = SecurityPermissions.InventoryAdjust)]
    [HttpPost("reservations")]
    public async Task<ActionResult<StockReservationResponse>> Reserve(
        ReserveStockRequest request,
        CancellationToken cancellationToken)
    {
        var reservation = await inventoryService.ReserveAsync(
            new ReserveStockCommand(
                request.InventoryItemId,
                request.InventoryUnitId,
                request.OrderId,
                request.ReservationKey,
                request.Quantity,
                request.LifetimeMinutes,
                request.InventoryRowVersion),
            cancellationToken);
        return Created($"/api/v1/inventory/reservations/{reservation.Id:D}", MapReservation(reservation));
    }

    [Authorize(Policy = SecurityPermissions.InventoryAdjust)]
    [HttpPost("reservations/{reservationId:guid}/release")]
    public async Task<ActionResult<StockReservationResponse>> ReleaseReservation(
        Guid reservationId,
        ChangeReservationRequest request,
        CancellationToken cancellationToken) =>
        Ok(MapReservation(await inventoryService.ReleaseReservationAsync(
            reservationId,
            request.ReservationRowVersion,
            request.InventoryRowVersion,
            cancellationToken)));

    [Authorize(Policy = SecurityPermissions.InventoryAdjust)]
    [HttpPost("reservations/{reservationId:guid}/confirm")]
    public async Task<ActionResult<StockReservationResponse>> ConfirmReservation(
        Guid reservationId,
        ChangeReservationRequest request,
        CancellationToken cancellationToken) =>
        Ok(MapReservation(await inventoryService.ConfirmReservationAsync(
            reservationId,
            request.ReservationRowVersion,
            request.InventoryRowVersion,
            cancellationToken)));

    [Authorize(Policy = SecurityPermissions.InventoryAdjust)]
    [HttpPost("transfers")]
    public async Task<IActionResult> TransferStock(
        TransferStockRequest request,
        CancellationToken cancellationToken)
    {
        await inventoryService.TransferStockAsync(
            new TransferStockCommand(
                request.SourceInventoryItemId,
                request.DestinationWarehouseId,
                request.Quantity,
                request.SourceRowVersion),
            cancellationToken);
        return NoContent();
    }

    [Authorize(Policy = SecurityPermissions.InventoryAdjust)]
    [HttpPost("unit-transfers")]
    public async Task<ActionResult<InventoryUnitResponse>> TransferInventoryUnit(
        TransferInventoryUnitRequest request,
        CancellationToken cancellationToken) =>
        Ok(MapUnit(await inventoryService.TransferInventoryUnitAsync(
            new TransferInventoryUnitCommand(
                request.InventoryUnitId,
                request.DestinationWarehouseId,
                request.UnitRowVersion,
                request.SourceInventoryRowVersion),
            cancellationToken)));

    private static InventoryItemResponse MapItem(InventoryItemInfo item) => new()
    {
        Id = item.Id,
        WarehouseId = item.WarehouseId,
        ProductVariantId = item.ProductVariantId,
        QuantityOnHand = item.QuantityOnHand,
        QuantityReserved = item.QuantityReserved,
        QuantityAvailable = item.QuantityAvailable,
        AverageUnitCostRials = item.AverageUnitCostRials,
        HasAcquisitionCost = item.HasAcquisitionCost,
        RowVersion = item.RowVersion
    };

    private static SupplierPurchaseResponse MapPurchase(SupplierPurchaseInfo purchase) => new()
    {
        Id = purchase.Id,
        PurchaseNumber = purchase.PurchaseNumber,
        SupplierId = purchase.SupplierId,
        SupplierName = purchase.SupplierName,
        WarehouseId = purchase.WarehouseId,
        WarehouseName = purchase.WarehouseName,
        ProductVariantId = purchase.ProductVariantId,
        ProductName = purchase.ProductName,
        VariantName = purchase.VariantName,
        Sku = purchase.Sku,
        InventoryItemId = purchase.InventoryItemId,
        Quantity = purchase.Quantity,
        UnitCostRials = purchase.UnitCostRials,
        TotalCostRials = purchase.TotalCostRials,
        SellingUnitPriceRials = purchase.SellingUnitPriceRials,
        ExpectedUnitProfitRials = purchase.ExpectedUnitProfitRials,
        ExpectedTotalProfitRials = purchase.ExpectedTotalProfitRials,
        PurchasedAt = purchase.PurchasedAt,
        SupplierReference = purchase.SupplierReference,
        Notes = purchase.Notes
    };

    private static WarehouseResponse MapWarehouse(WarehouseInfo warehouse) => new()
    {
        Id = warehouse.Id,
        Code = warehouse.Code,
        Name = warehouse.Name,
        IsActive = warehouse.IsActive,
        RowVersion = warehouse.RowVersion
    };

    private static InventoryUnitResponse MapUnit(InventoryUnitInfo unit) => new()
    {
        Id = unit.Id,
        ProductId = unit.ProductId,
        ProductVariantId = unit.ProductVariantId,
        WarehouseId = unit.WarehouseId,
        InventoryItemId = unit.InventoryItemId,
        SerialNumber = unit.SerialNumber,
        Barcode = unit.Barcode,
        ActualGrossWeight = unit.ActualGrossWeight,
        ActualNetGoldWeight = unit.ActualNetGoldWeight,
        Karat = unit.Karat,
        Status = unit.Status.ToString(),
        ReceivedAt = unit.ReceivedAt,
        SoldAt = unit.SoldAt,
        RowVersion = unit.RowVersion
    };

    private static StockReservationResponse MapReservation(StockReservationInfo reservation) => new()
    {
        Id = reservation.Id,
        InventoryItemId = reservation.InventoryItemId,
        InventoryUnitId = reservation.InventoryUnitId,
        OrderId = reservation.OrderId,
        ReservationKey = reservation.ReservationKey,
        Quantity = reservation.Quantity,
        Status = reservation.Status.ToString(),
        ExpiresAt = reservation.ExpiresAt,
        RowVersion = reservation.RowVersion
    };

    private static StockMovementResponse MapMovement(StockMovementInfo movement) => new()
    {
        Id = movement.Id,
        InventoryItemId = movement.InventoryItemId,
        InventoryUnitId = movement.InventoryUnitId,
        MovementType = movement.MovementType.ToString(),
        QuantityDelta = movement.QuantityDelta,
        BalanceAfter = movement.BalanceAfter,
        ReservedQuantityDelta = movement.ReservedQuantityDelta,
        ReservedBalanceAfter = movement.ReservedBalanceAfter,
        ReferenceType = movement.ReferenceType,
        ReferenceId = movement.ReferenceId,
        Reason = movement.Reason,
        OccurredAt = movement.OccurredAt
    };
}
