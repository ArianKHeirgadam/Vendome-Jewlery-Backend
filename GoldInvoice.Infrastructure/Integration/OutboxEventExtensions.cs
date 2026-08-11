using GoldInvoice.Application.Integration;
using GoldInvoice.Application.Security;
using GoldInvoice.Domain.Inventory;
using GoldInvoice.Domain.Invoicing;
using GoldInvoice.Domain.Orders;
using GoldInvoice.Domain.Pricing;

namespace GoldInvoice.Infrastructure.Integration;

internal static class OutboxEventExtensions
{
    public static Guid AddInvoiceCreated(
        this IOutboxWriter writer,
        Invoice invoice,
        DateTimeOffset occurredAt) =>
        writer.Add(new IntegrationEventDefinition(
            IntegrationEventTypes.InvoiceCreatedV1,
            nameof(Invoice),
            invoice.Id,
            occurredAt,
            new InvoiceCreatedV1(
                invoice.Id,
                invoice.OrderId,
                invoice.PaymentId!.Value,
                invoice.InvoiceNumber,
                invoice.Status.ToString()),
            ForCustomerAndOperations(invoice.CustomerId)));

    public static Guid AddInventoryChanged(
        this IOutboxWriter writer,
        InventoryItem item,
        StockMovement movement) =>
        writer.Add(new IntegrationEventDefinition(
            IntegrationEventTypes.InventoryChangedV1,
            nameof(InventoryItem),
            item.Id,
            movement.OccurredAt,
            new InventoryChangedV1(
                item.Id,
                item.WarehouseId,
                item.ProductVariantId,
                movement.InventoryUnitId,
                movement.Id,
                movement.MovementType.ToString(),
                item.QuantityOnHand,
                item.QuantityReserved,
                item.QuantityAvailable),
            ForRoles(SecurityRoles.Owner, SecurityRoles.Admin)));

    public static Guid AddOrderStatusChanged(
        this IOutboxWriter writer,
        Order order,
        OrderStatus? fromStatus,
        DateTimeOffset occurredAt) =>
        writer.Add(new IntegrationEventDefinition(
            IntegrationEventTypes.OrderStatusChangedV1,
            nameof(Order),
            order.Id,
            occurredAt,
            new OrderStatusChangedV1(
                order.Id,
                order.CustomerId,
                fromStatus?.ToString(),
                order.Status.ToString()),
            ForCustomerAndOperations(order.CustomerId)));

    public static Guid AddMarketPriceUpdated(
        this IOutboxWriter writer,
        MarketPriceSnapshot snapshot) =>
        writer.Add(new IntegrationEventDefinition(
            IntegrationEventTypes.MarketPriceUpdatedV1,
            nameof(MarketPriceSnapshot),
            snapshot.Id,
            snapshot.CapturedAt,
            new MarketPriceUpdatedV1(
                snapshot.Id,
                snapshot.SourceId,
                snapshot.PriceType.ToString(),
                snapshot.BuyPriceRials,
                snapshot.SellPriceRials,
                snapshot.ProviderTimestamp),
            ForRoles(SecurityRoles.Owner, SecurityRoles.Admin, SecurityRoles.Customer)));

    private static IntegrationEventAudience ForCustomerAndOperations(Guid customerId) => new(
        [customerId],
        [SecurityRoles.Owner, SecurityRoles.Admin],
        []);

    private static IntegrationEventAudience ForRoles(params string[] roles) => new([], roles, []);
}
