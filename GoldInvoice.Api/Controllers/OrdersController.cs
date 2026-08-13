using GoldInvoice.Api.Security;
using GoldInvoice.Application.Orders;
using GoldInvoice.Application.Security;
using GoldInvoice.Contracts.Common;
using GoldInvoice.Contracts.Orders;
using GoldInvoice.Domain.Orders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GoldInvoice.Api.Controllers;

[ApiController]
[Authorize]
[RequestSizeLimit(128 * 1024)]
[Route("api/v1/orders")]
public sealed class OrdersController(IOrderService orderService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResponse<OrderResponse>>> GetOrders(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default)
    {
        var actorUserId = User.GetRequiredUserId();
        var result = await orderService.GetOrdersAsync(
            actorUserId,
            CanReadAll(),
            page,
            pageSize,
            ParseOptionalStatus(status),
            cancellationToken);
        return Ok(new PagedResponse<OrderResponse>
        {
            Items = result.Items.Select(Map).ToArray(),
            Page = result.Page,
            PageSize = result.PageSize,
            TotalCount = result.TotalCount
        });
    }

    [HttpGet("{orderId:guid}")]
    public async Task<ActionResult<OrderResponse>> GetOrder(
        Guid orderId,
        CancellationToken cancellationToken) =>
        Ok(Map(await orderService.GetOrderAsync(
            orderId,
            User.GetRequiredUserId(),
            CanReadAll(),
            cancellationToken)));

    [HttpPost]
    public async Task<ActionResult<OrderResponse>> CreateOrder(
        CreateOrderRequest request,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var actorUserId = User.GetRequiredUserId();
        var customerId = request.CustomerId == Guid.Empty ? actorUserId : request.CustomerId;
        var order = await orderService.CreateOrderAsync(
            new CreateOrderCommand(
                actorUserId,
                customerId,
                User.HasPermission(SecurityPermissions.OrdersManage),
                request.CustomerAddressId,
                request.CustomerNationalId,
                request.Lines.Select(line => new CreateOrderLineCommand(
                    line.InventoryItemId,
                    line.InventoryUnitId,
                    line.Quantity,
                    line.ActualGrossWeight,
                    line.ActualNetGoldWeight,
                    line.InventoryRowVersion,
                    line.InventoryUnitRowVersion)).ToArray(),
                request.ReservationLifetimeMinutes,
                request.DiscountRials,
                request.ShippingRials,
                idempotencyKey),
            cancellationToken);
        return CreatedAtAction(nameof(GetOrder), new { orderId = order.Id }, Map(order));
    }

    [HttpPost("{orderId:guid}/cancel")]
    public async Task<ActionResult<OrderResponse>> CancelOrder(
        Guid orderId,
        CancelOrderRequest request,
        CancellationToken cancellationToken) =>
        Ok(Map(await orderService.CancelOrderAsync(
            orderId,
            new CancelOrderCommand(
                User.GetRequiredUserId(),
                User.HasPermission(SecurityPermissions.OrdersManage),
                request.Reason,
                request.RowVersion),
            cancellationToken)));

    [Authorize(Policy = SecurityPermissions.OrdersManage)]
    [HttpPost("{orderId:guid}/status")]
    public async Task<ActionResult<OrderResponse>> ChangeStatus(
        Guid orderId,
        ChangeOrderStatusRequest request,
        CancellationToken cancellationToken) =>
        Ok(Map(await orderService.ChangeStatusAsync(
            orderId,
            new ChangeOrderStatusCommand(
                User.GetRequiredUserId(),
                ParseStatus(request.TargetStatus),
                request.Reason,
                request.RowVersion),
            cancellationToken)));

    private bool CanReadAll() =>
        User.HasPermission(SecurityPermissions.OrdersRead) ||
        User.HasPermission(SecurityPermissions.OrdersManage);

    private static OrderResponse Map(OrderInfo order) => new()
    {
        Id = order.Id,
        CustomerId = order.CustomerId,
        OrderNumber = order.OrderNumber,
        Status = order.Status.ToString(),
        ItemsSubtotalRials = order.ItemsSubtotalRials,
        DiscountRials = order.DiscountRials,
        ShippingRials = order.ShippingRials,
        GrandTotalRials = order.GrandTotalRials,
        CustomerNameSnapshot = order.CustomerNameSnapshot,
        CustomerNationalIdSnapshot = order.CustomerNationalIdSnapshot,
        PaidAt = order.PaidAt,
        CancelledAt = order.CancelledAt,
        Address = order.Address is null ? null : new OrderAddressSnapshotResponse
        {
            Id = order.Address.Id,
            CustomerAddressId = order.Address.CustomerAddressId,
            RecipientName = order.Address.RecipientName,
            PhoneNumber = order.Address.PhoneNumber,
            Province = order.Address.Province,
            City = order.Address.City,
            PostalCode = order.Address.PostalCode,
            AddressLine = order.Address.AddressLine
        },
        Store = order.Store is null ? null : MapStore(order.Store),
        Items = order.Items.Select(item => new OrderItemResponse
        {
            Id = item.Id,
            LineNumber = item.LineNumber,
            ProductVariantId = item.ProductVariantId,
            InventoryItemId = item.InventoryItemId,
            InventoryUnitId = item.InventoryUnitId,
            PriceCalculationSnapshotId = item.PriceCalculationSnapshotId,
            StockReservationId = item.StockReservationId,
            Sku = item.Sku,
            ProductName = item.ProductName,
            VariantName = item.VariantName,
            GrossWeightGrams = item.GrossWeightGrams,
            NetGoldWeightGrams = item.NetGoldWeightGrams,
            Karat = item.Karat,
            Quantity = item.Quantity,
            MarketUnitPriceRials = item.MarketUnitPriceRials,
            GoldValueRials = item.GoldValueRials,
            WageRials = item.WageRials,
            ProfitRials = item.ProfitRials,
            TaxRials = item.TaxRials,
            UnitPriceRials = item.UnitPriceRials,
            LineTotalRials = item.LineTotalRials,
            AcquisitionUnitCostRials = item.AcquisitionUnitCostRials,
            AcquisitionTotalCostRials = item.AcquisitionTotalCostRials,
            GrossProfitRials = item.GrossProfitRials,
            RoundingPolicy = item.RoundingPolicy
        }).ToArray(),
        RowVersion = order.RowVersion
    };

    internal static StoreIdentitySnapshotResponse MapStore(StoreIdentitySnapshotInfo store) => new()
    {
        Id = store.Id,
        TradeName = store.TradeName,
        LegalName = store.LegalName,
        NationalId = store.NationalId,
        EconomicCode = store.EconomicCode,
        RegistrationNumber = store.RegistrationNumber,
        PhoneNumber = store.PhoneNumber,
        PostalCode = store.PostalCode,
        AddressLine = store.AddressLine
    };

    private static OrderStatus? ParseOptionalStatus(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : ParseStatus(value);

    private static OrderStatus ParseStatus(string value) =>
        Enum.TryParse<OrderStatus>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new ArgumentException("The order status is invalid.", nameof(value));
}
