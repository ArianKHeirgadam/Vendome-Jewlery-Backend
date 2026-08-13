using GoldInvoice.Application.Common;
using GoldInvoice.Domain.Orders;

namespace GoldInvoice.Application.Orders;

public sealed record OrderAddressSnapshotInfo(
    Guid Id,
    Guid? CustomerAddressId,
    string RecipientName,
    string PhoneNumber,
    string Province,
    string City,
    string PostalCode,
    string AddressLine);

public sealed record StoreIdentitySnapshotInfo(
    Guid Id,
    string TradeName,
    string LegalName,
    string? NationalId,
    string? EconomicCode,
    string? RegistrationNumber,
    string PhoneNumber,
    string PostalCode,
    string AddressLine);

public sealed record OrderItemInfo(
    Guid Id,
    int LineNumber,
    Guid ProductVariantId,
    Guid? InventoryItemId,
    Guid? InventoryUnitId,
    Guid? PriceCalculationSnapshotId,
    Guid? StockReservationId,
    string Sku,
    string ProductName,
    string VariantName,
    decimal GrossWeightGrams,
    decimal? NetGoldWeightGrams,
    int? Karat,
    int Quantity,
    long? MarketUnitPriceRials,
    long? GoldValueRials,
    long? WageRials,
    long? ProfitRials,
    long? TaxRials,
    long UnitPriceRials,
    long LineTotalRials,
    long? AcquisitionUnitCostRials,
    long? AcquisitionTotalCostRials,
    long? GrossProfitRials,
    string? RoundingPolicy);

public sealed record OrderInfo(
    Guid Id,
    Guid CustomerId,
    string OrderNumber,
    OrderStatus Status,
    long ItemsSubtotalRials,
    long DiscountRials,
    long ShippingRials,
    long GrandTotalRials,
    string? CustomerNameSnapshot,
    string? CustomerNationalIdSnapshot,
    DateTimeOffset? PaidAt,
    DateTimeOffset? CancelledAt,
    OrderAddressSnapshotInfo? Address,
    StoreIdentitySnapshotInfo? Store,
    IReadOnlyList<OrderItemInfo> Items,
    string RowVersion);

public sealed record CreateOrderLineCommand(
    Guid InventoryItemId,
    Guid? InventoryUnitId,
    int Quantity,
    decimal? ActualGrossWeight,
    decimal? ActualNetGoldWeight,
    string InventoryRowVersion,
    string? InventoryUnitRowVersion);

public sealed record CreateOrderCommand(
    Guid ActorUserId,
    Guid CustomerId,
    bool CanManageOrders,
    Guid CustomerAddressId,
    string? CustomerNationalId,
    IReadOnlyList<CreateOrderLineCommand> Lines,
    int ReservationLifetimeMinutes,
    long DiscountRials,
    long ShippingRials,
    string IdempotencyKey);

public sealed record CancelOrderCommand(
    Guid ActorUserId,
    bool CanManageOrders,
    string Reason,
    string RowVersion);

public sealed record ChangeOrderStatusCommand(
    Guid ActorUserId,
    OrderStatus TargetStatus,
    string? Reason,
    string RowVersion);

public interface IOrderService
{
    Task<PagedResult<OrderInfo>> GetOrdersAsync(
        Guid actorUserId,
        bool canReadAll,
        int page,
        int pageSize,
        OrderStatus? status,
        CancellationToken cancellationToken);

    Task<OrderInfo> GetOrderAsync(
        Guid orderId,
        Guid actorUserId,
        bool canReadAll,
        CancellationToken cancellationToken);

    Task<OrderInfo> CreateOrderAsync(
        CreateOrderCommand command,
        CancellationToken cancellationToken);

    Task<OrderInfo> CancelOrderAsync(
        Guid orderId,
        CancelOrderCommand command,
        CancellationToken cancellationToken);

    Task<OrderInfo> ChangeStatusAsync(
        Guid orderId,
        ChangeOrderStatusCommand command,
        CancellationToken cancellationToken);
}
