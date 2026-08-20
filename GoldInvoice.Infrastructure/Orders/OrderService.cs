using System.Text.Json;
using GoldInvoice.Application.Common;
using GoldInvoice.Application.Integration;
using GoldInvoice.Application.Orders;
using GoldInvoice.Application.Pricing;
using GoldInvoice.Application.Settings;
using GoldInvoice.Domain.Catalog;
using GoldInvoice.Domain.Inventory;
using GoldInvoice.Domain.Orders;
using GoldInvoice.Domain.Payments;
using GoldInvoice.Domain.Platform;
using GoldInvoice.Infrastructure.Inventory;
using GoldInvoice.Infrastructure.Integration;
using GoldInvoice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoldInvoice.Infrastructure.Orders;

internal sealed class OrderService(
    GoldInvoiceDbContext dbContext,
    IProductPricingService pricingService,
    IStoreProfileService storeProfileService,
    InventoryReservationCoordinator reservationCoordinator,
    IOutboxWriter outboxWriter,
    TimeProvider timeProvider) : IOrderService
{
    private const int MaximumPageSize = 100;
    private const int MaximumOrderLines = 100;

    public async Task<PagedResult<OrderInfo>> GetOrdersAsync(
        Guid actorUserId,
        bool canReadAll,
        int page,
        int pageSize,
        OrderStatus? status,
        CancellationToken cancellationToken)
    {
        ValidateActor(actorUserId);
        ValidatePage(page, pageSize);
        var query = dbContext.Orders.AsNoTracking();
        if (!canReadAll)
        {
            query = query.Where(order => order.CustomerId == actorUserId);
        }

        if (status is not null)
        {
            query = query.Where(order => order.Status == status);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var orders = await query
            .OrderByDescending(order => order.CreatedAt)
            .ThenByDescending(order => order.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<OrderInfo>(
            await MapOrdersAsync(orders, cancellationToken),
            page,
            pageSize,
            totalCount);
    }

    public async Task<OrderInfo> GetOrderAsync(
        Guid orderId,
        Guid actorUserId,
        bool canReadAll,
        CancellationToken cancellationToken)
    {
        ValidateActor(actorUserId);
        var order = await dbContext.Orders
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == orderId, cancellationToken) ??
            throw new ApplicationResourceNotFoundException();
        EnsureAccess(order, actorUserId, canReadAll);
        return AssertSingle(await MapOrdersAsync([order], cancellationToken));
    }

    public async Task<OrderInfo> CreateOrderAsync(
        CreateOrderCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateCreateCommand(command);
        EnsureCustomerAccess(command.ActorUserId, command.CustomerId, command.CanManageOrders);
        StoreProfileInfo store;
        try
        {
            store = await storeProfileService.GetAsync(cancellationToken);
        }
        catch (ApplicationResourceNotFoundException)
        {
            throw new StoreProfileNotConfiguredException();
        }
        var idempotencyKey = PersistenceUtilities.NormalizeIdempotencyKey(command.IdempotencyKey);
        var scope = $"Orders.Create:{command.ActorUserId:N}";
        var keyHash = PersistenceUtilities.Hash(idempotencyKey);
        var requestHash = CreateRequestHash(command);

        await using var transaction = await PersistenceUtilities.BeginSerializableTransactionAsync(
            dbContext,
            cancellationToken);
        var existingRecord = await dbContext.IdempotencyRecords.SingleOrDefaultAsync(
            record => record.Scope == scope && record.KeyHash == keyHash,
            cancellationToken);
        if (existingRecord is not null)
        {
            if (!string.Equals(existingRecord.RequestHash, requestHash, StringComparison.Ordinal) ||
                existingRecord.Status != IdempotencyRecordStatus.Completed ||
                !Guid.TryParse(existingRecord.ResponseBody, out var existingOrderId))
            {
                throw new ApplicationConflictException();
            }

            await PersistenceUtilities.CommitAsync(transaction, cancellationToken);
            return await GetOrderAsync(
                existingOrderId,
                command.ActorUserId,
                command.CanManageOrders,
                cancellationToken);
        }

        var now = timeProvider.GetUtcNow();
        var idempotencyRecord = new IdempotencyRecord(
            scope,
            keyHash,
            requestHash,
            now.AddHours(24));
        dbContext.IdempotencyRecords.Add(idempotencyRecord);

        var customer = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(
                user => user.Id == command.CustomerId && user.IsActive,
                cancellationToken) ?? throw new ApplicationResourceNotFoundException();
        var address = await dbContext.CustomerAddresses
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == command.CustomerAddressId &&
                    candidate.CustomerId == command.CustomerId,
                cancellationToken) ?? throw new ApplicationResourceNotFoundException();
        var itemIds = command.Lines.Select(line => line.InventoryItemId).Distinct().ToArray();
        var items = await dbContext.InventoryItems
            .Where(item => itemIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        if (items.Count != itemIds.Length)
        {
            throw new ApplicationResourceNotFoundException();
        }

        var unitIds = command.Lines
            .Where(line => line.InventoryUnitId is not null)
            .Select(line => line.InventoryUnitId!.Value)
            .ToArray();
        var units = await dbContext.InventoryUnits
            .Where(unit => unitIds.Contains(unit.Id))
            .ToDictionaryAsync(unit => unit.Id, cancellationToken);
        if (units.Count != unitIds.Length)
        {
            throw new ApplicationResourceNotFoundException();
        }

        var variantIds = items.Values.Select(item => item.ProductVariantId).Distinct().ToArray();
        var variants = await dbContext.ProductVariants
            .AsNoTracking()
            .Where(variant => variantIds.Contains(variant.Id))
            .ToDictionaryAsync(variant => variant.Id, cancellationToken);
        var productIds = variants.Values.Select(variant => variant.ProductId).Distinct().ToArray();
        var products = await dbContext.Products
            .AsNoTracking()
            .Where(product => productIds.Contains(product.Id))
            .ToDictionaryAsync(product => product.Id, cancellationToken);
        var details = await dbContext.GoldProductDetails
            .AsNoTracking()
            .Where(detail => variantIds.Contains(detail.ProductVariantId))
            .ToDictionaryAsync(detail => detail.ProductVariantId, cancellationToken);
        if (variants.Count != variantIds.Length ||
            products.Count != productIds.Length ||
            details.Count != variantIds.Length)
        {
            throw new ApplicationConflictException();
        }

        var drafts = new List<OrderLineDraft>(command.Lines.Count);
        long subtotal = 0;
        for (var index = 0; index < command.Lines.Count; index++)
        {
            var line = command.Lines[index];
            var item = items[line.InventoryItemId];
            PersistenceUtilities.SetOriginalRowVersion(dbContext, item, line.InventoryRowVersion);
            var variant = variants[item.ProductVariantId];
            var product = products[variant.ProductId];
            var detail = details[variant.Id];
            if (!variant.IsActive || !product.IsActive)
            {
                throw new ApplicationConflictException();
            }

            InventoryUnit? unit = null;
            decimal? actualGrossWeight = line.ActualGrossWeight;
            decimal? actualNetGoldWeight = line.ActualNetGoldWeight;
            if (line.InventoryUnitId is not null)
            {
                unit = units[line.InventoryUnitId.Value];
                if (unit.InventoryItemId != item.Id ||
                    unit.ProductVariantId != item.ProductVariantId ||
                    line.Quantity != 1 ||
                    line.InventoryUnitRowVersion is null ||
                    (line.ActualGrossWeight is not null && line.ActualGrossWeight != unit.ActualGrossWeight) ||
                    (line.ActualNetGoldWeight is not null && line.ActualNetGoldWeight != unit.ActualNetGoldWeight))
                {
                    throw new ApplicationConflictException();
                }

                PersistenceUtilities.SetOriginalRowVersion(
                    dbContext,
                    unit,
                    line.InventoryUnitRowVersion);
                actualGrossWeight = unit.ActualGrossWeight;
                actualNetGoldWeight = unit.ActualNetGoldWeight;
            }
            else if (!string.IsNullOrWhiteSpace(line.InventoryUnitRowVersion) ||
                     (detail.IsWeightVariable && line.Quantity != 1))
            {
                throw new ArgumentException(
                    "Variable-weight or individually tracked jewelry must be ordered one physical piece per line.",
                    nameof(command));
            }

            var calculated = await pricingService.CalculateAsync(
                new CalculateProductPriceCommand(
                    variant.Id,
                    detail.IsWeightVariable ? actualGrossWeight : null,
                    detail.IsWeightVariable ? actualNetGoldWeight : null),
                cancellationToken);
            var grossWeight = detail.IsWeightVariable
                ? actualGrossWeight!.Value
                : detail.GrossWeight;
            var netGoldWeight = detail.IsWeightVariable
                ? actualNetGoldWeight!.Value
                : detail.NetGoldWeight;
            var lineTotal = checked(calculated.FinalPriceRials * line.Quantity);
            subtotal = checked(subtotal + lineTotal);
            drafts.Add(new OrderLineDraft(
                index + 1,
                item,
                unit,
                variant,
                product,
                detail,
                calculated,
                grossWeight,
                netGoldWeight,
                line.Quantity));
        }

        var order = new Order(
            command.CustomerId,
            CreateOrderNumber(now),
            subtotal,
            command.DiscountRials,
            command.ShippingRials,
            customer.DisplayName,
            command.CustomerNationalId);
        dbContext.Orders.Add(order);
        dbContext.OrderAddressSnapshots.Add(new OrderAddressSnapshot(
            order.Id,
            address.RecipientName,
            address.PhoneNumber,
            address.Province,
            address.City,
            address.PostalCode,
            address.AddressLine,
            address.Id));
        dbContext.OrderStoreSnapshots.Add(new OrderStoreSnapshot(
            order.Id,
            store.TradeName,
            store.LegalName,
            store.NationalId,
            store.EconomicCode,
            store.RegistrationNumber,
            store.PhoneNumber,
            store.PostalCode,
            store.AddressLine));
        dbContext.OrderStatusHistory.Add(new OrderStatusHistory(
            order.Id,
            null,
            OrderStatus.Pending,
            now,
            command.ActorUserId));
        outboxWriter.AddOrderStatusChanged(order, fromStatus: null, now);

        var expiresAt = now.AddMinutes(command.ReservationLifetimeMinutes);
        foreach (var draft in drafts)
        {
            var item = new OrderItem(
                order.Id,
                draft.Variant.Id,
                draft.LineNumber,
                draft.Variant.Sku,
                draft.Product.Name,
                draft.Variant.Name,
                draft.GrossWeight,
                ToPurity(draft.Detail.Karat),
                draft.Price.FinalPriceRials,
                draft.Quantity,
                draft.Price.SnapshotId,
                draft.InventoryItem.Id,
                draft.InventoryUnit?.Id,
                draft.NetGoldWeight,
                draft.Detail.Karat,
                draft.Price.MarketUnitPriceRials,
                draft.Price.GoldValueRials,
                draft.Price.WageRials,
                draft.Price.ProfitRials,
                draft.Price.TaxRials,
                draft.Price.RoundingPolicy,
                draft.InventoryUnit?.AcquisitionCostRials ??
                    (draft.InventoryItem.HasAcquisitionCost
                        ? draft.InventoryItem.AverageUnitCostRials
                        : null));
            dbContext.OrderItems.Add(item);
            reservationCoordinator.Reserve(
                item,
                draft.InventoryItem,
                draft.InventoryUnit,
                draft.Quantity,
                expiresAt);
        }

        order.MoveToAwaitingPayment();
        dbContext.OrderStatusHistory.Add(new OrderStatusHistory(
            order.Id,
            OrderStatus.Pending,
            OrderStatus.AwaitingPayment,
            now,
            command.ActorUserId));
        outboxWriter.AddOrderStatusChanged(order, OrderStatus.Pending, now);
        idempotencyRecord.Complete(201, order.Id.ToString("D"), now);
        await PersistenceUtilities.SaveChangesAsync(dbContext, cancellationToken);
        await PersistenceUtilities.CommitAsync(transaction, cancellationToken);
        return await GetOrderAsync(
            order.Id,
            command.ActorUserId,
            command.CanManageOrders,
            cancellationToken);
    }

    public async Task<OrderInfo> CancelOrderAsync(
        Guid orderId,
        CancelOrderCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateActor(command.ActorUserId);
        if (string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Trim().Length > 1000)
        {
            throw new ArgumentException("A cancellation reason is required.", nameof(command));
        }

        await using var transaction = await PersistenceUtilities.BeginSerializableTransactionAsync(
            dbContext,
            cancellationToken);
        var order = await dbContext.Orders.FindAsync([orderId], cancellationToken) ??
            throw new ApplicationResourceNotFoundException();
        EnsureAccess(order, command.ActorUserId, command.CanManageOrders);
        if (order.Status == OrderStatus.PaymentReview && !command.CanManageOrders)
        {
            throw new ApplicationConflictException();
        }

        PersistenceUtilities.SetOriginalRowVersion(dbContext, order, command.RowVersion);
        var fromStatus = order.Status;
        var now = timeProvider.GetUtcNow();
        var payments = await dbContext.Payments
            .Where(payment => payment.OrderId == order.Id &&
                (payment.Status == PaymentStatus.Pending ||
                 payment.Status == PaymentStatus.Processing ||
                 payment.Status == PaymentStatus.RequiresReview))
            .ToListAsync(cancellationToken);
        if (payments.Any(payment =>
                payment.Status is PaymentStatus.Pending or PaymentStatus.Processing))
        {
            // An in-flight gateway transaction can complete at any moment;
            // cancelling now would strand the customer's money with the
            // provider (the late callback would be rejected as a final-state
            // mismatch and the order would end cancelled). Let the payment
            // finish or fail on its own first.
            throw new ApplicationConflictException();
        }

        order.Cancel(now);
        await reservationCoordinator.ReleaseForCancellationAsync(
            order.Id,
            command.Reason,
            cancellationToken);
        foreach (var payment in payments)
        {
            payment.Cancel(now);
        }

        dbContext.OrderStatusHistory.Add(new OrderStatusHistory(
            order.Id,
            fromStatus,
            OrderStatus.Cancelled,
            now,
            command.ActorUserId,
            command.Reason));
        outboxWriter.AddOrderStatusChanged(order, fromStatus, now);
        await PersistenceUtilities.SaveChangesAsync(dbContext, cancellationToken);
        await PersistenceUtilities.CommitAsync(transaction, cancellationToken);
        return await GetOrderAsync(
            order.Id,
            command.ActorUserId,
            command.CanManageOrders,
            cancellationToken);
    }

    public async Task<OrderInfo> ChangeStatusAsync(
        Guid orderId,
        ChangeOrderStatusCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateActor(command.ActorUserId);
        if (command.TargetStatus is not OrderStatus.Processing and not OrderStatus.Completed)
        {
            throw new ArgumentException(
                "Only processing and completion are manual Phase 5 transitions.",
                nameof(command.TargetStatus));
        }

        await using var transaction = await PersistenceUtilities.BeginSerializableTransactionAsync(
            dbContext,
            cancellationToken);
        var order = await dbContext.Orders.FindAsync([orderId], cancellationToken) ??
            throw new ApplicationResourceNotFoundException();
        PersistenceUtilities.SetOriginalRowVersion(dbContext, order, command.RowVersion);
        var fromStatus = order.Status;
        if (command.TargetStatus == OrderStatus.Processing)
        {
            order.MoveToProcessing();
        }
        else
        {
            order.Complete();
        }

        var changedAt = timeProvider.GetUtcNow();
        dbContext.OrderStatusHistory.Add(new OrderStatusHistory(
            order.Id,
            fromStatus,
            order.Status,
            changedAt,
            command.ActorUserId,
            command.Reason));
        outboxWriter.AddOrderStatusChanged(order, fromStatus, changedAt);
        await PersistenceUtilities.SaveChangesAsync(dbContext, cancellationToken);
        await PersistenceUtilities.CommitAsync(transaction, cancellationToken);
        return await GetOrderAsync(order.Id, command.ActorUserId, canReadAll: true, cancellationToken);
    }

    private async Task<IReadOnlyList<OrderInfo>> MapOrdersAsync(
        IReadOnlyList<Order> orders,
        CancellationToken cancellationToken)
    {
        if (orders.Count == 0)
        {
            return [];
        }

        var orderIds = orders.Select(order => order.Id).ToArray();
        var items = await dbContext.OrderItems
            .AsNoTracking()
            .Where(item => orderIds.Contains(item.OrderId))
            .OrderBy(item => item.LineNumber)
            .ToListAsync(cancellationToken);
        var reservations = await dbContext.StockReservations
            .AsNoTracking()
            .Where(reservation => orderIds.Contains(reservation.OrderId) && reservation.OrderItemId != null)
            .ToDictionaryAsync(reservation => reservation.OrderItemId!.Value, cancellationToken);
        var addresses = await dbContext.OrderAddressSnapshots
            .AsNoTracking()
            .Where(address => orderIds.Contains(address.OrderId))
            .ToDictionaryAsync(address => address.OrderId, cancellationToken);
        var stores = await dbContext.OrderStoreSnapshots
            .AsNoTracking()
            .Where(store => orderIds.Contains(store.OrderId))
            .ToDictionaryAsync(store => store.OrderId, cancellationToken);
        var itemGroups = items.ToLookup(item => item.OrderId);

        return orders.Select(order => new OrderInfo(
            order.Id,
            order.CustomerId,
            order.OrderNumber,
            order.Status,
            order.ItemsSubtotalRials,
            order.DiscountRials,
            order.ShippingRials,
            order.GrandTotalRials,
            order.CustomerNameSnapshot,
            order.CustomerNationalIdSnapshot,
            order.PaidAt,
            order.CancelledAt,
            addresses.TryGetValue(order.Id, out var address) ? MapAddress(address) : null,
            stores.TryGetValue(order.Id, out var store) ? MapStore(store) : null,
            itemGroups[order.Id].Select(item => MapItem(item, reservations)).ToArray(),
            Convert.ToBase64String(order.RowVersion))).ToArray();
    }

    private static OrderItemInfo MapItem(
        OrderItem item,
        IReadOnlyDictionary<Guid, StockReservation> reservations) => new(
        item.Id,
        item.LineNumber,
        item.ProductVariantId,
        item.InventoryItemId,
        item.InventoryUnitId,
        item.PriceCalculationSnapshotId,
        reservations.TryGetValue(item.Id, out var reservation) ? reservation.Id : null,
        item.Sku,
        item.ProductName,
        item.VariantName,
        item.WeightGrams,
        item.NetGoldWeightGrams,
        item.Karat,
        item.Quantity,
        item.MarketUnitPriceRials,
        item.GoldValueRials,
        item.WageRials,
        item.ProfitRials,
        item.TaxRials,
        item.UnitPriceRials,
        item.LineTotalRials,
        item.AcquisitionUnitCostRials,
        item.AcquisitionTotalCostRials,
        item.GrossProfitRials,
        item.RoundingPolicy);

    private static OrderAddressSnapshotInfo MapAddress(OrderAddressSnapshot address) => new(
        address.Id,
        address.CustomerAddressId,
        address.RecipientName,
        address.PhoneNumber,
        address.Province,
        address.City,
        address.PostalCode,
        address.AddressLine);

    internal static StoreIdentitySnapshotInfo MapStore(OrderStoreSnapshot store) => new(
        store.Id,
        store.TradeName,
        store.LegalName,
        store.NationalId,
        store.EconomicCode,
        store.RegistrationNumber,
        store.PhoneNumber,
        store.PostalCode,
        store.AddressLine);

    private static void ValidateCreateCommand(CreateOrderCommand command)
    {
        ValidateActor(command.ActorUserId);
        if (command.CustomerId == Guid.Empty || command.CustomerAddressId == Guid.Empty)
        {
            throw new ArgumentException("A customer and address are required.", nameof(command));
        }

        if (command.Lines is null || command.Lines.Count is < 1 or > MaximumOrderLines)
        {
            throw new ArgumentOutOfRangeException(nameof(command.Lines));
        }

        if (command.ReservationLifetimeMinutes is < 1 or > 60 ||
            command.DiscountRials < 0 ||
            command.ShippingRials < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(command));
        }

        if (!command.CanManageOrders && (command.DiscountRials != 0 || command.ShippingRials != 0))
        {
            throw new ApplicationConflictException();
        }

        if (command.Lines.Any(line =>
                line.InventoryItemId == Guid.Empty ||
                line.InventoryUnitId == Guid.Empty ||
                line.Quantity <= 0) ||
            command.Lines.Where(line => line.InventoryUnitId is null)
                .Select(line => line.InventoryItemId)
                .Distinct()
                .Count() != command.Lines.Count(line => line.InventoryUnitId is null) ||
            command.Lines.Where(line => line.InventoryUnitId is not null)
                .Select(line => line.InventoryUnitId)
                .Distinct()
                .Count() != command.Lines.Count(line => line.InventoryUnitId is not null))
        {
            throw new ArgumentException("Order lines contain invalid or duplicate inventory identifiers.", nameof(command));
        }
    }

    private static void ValidatePage(int page, int pageSize)
    {
        if (page < 1 ||
            pageSize is < 1 or > MaximumPageSize ||
            ((long)page - 1) * pageSize > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(page));
        }
    }

    private static void ValidateActor(Guid actorUserId)
    {
        if (actorUserId == Guid.Empty)
        {
            throw new ArgumentException("A valid actor identifier is required.", nameof(actorUserId));
        }
    }

    private static void EnsureCustomerAccess(Guid actorUserId, Guid customerId, bool canManageOrders)
    {
        if (!canManageOrders && actorUserId != customerId)
        {
            throw new ApplicationResourceNotFoundException();
        }
    }

    private static void EnsureAccess(Order order, Guid actorUserId, bool canReadAll) =>
        EnsureCustomerAccess(actorUserId, order.CustomerId, canReadAll);

    private static string CreateRequestHash(CreateOrderCommand command) =>
        PersistenceUtilities.Hash(JsonSerializer.Serialize(new
        {
            command.CustomerId,
            command.CustomerAddressId,
            command.CustomerNationalId,
            command.ReservationLifetimeMinutes,
            command.DiscountRials,
            command.ShippingRials,
            command.Lines
        }));

    private static string CreateOrderNumber(DateTimeOffset now) =>
        $"ORD-{now:yyyyMMdd}-{Guid.NewGuid():N}".ToUpperInvariant();

    private static int ToPurity(int karat) =>
        checked((int)Math.Round(karat * 1000m / 24m, MidpointRounding.AwayFromZero));

    private static T AssertSingle<T>(IReadOnlyList<T> values) =>
        values.Count == 1 ? values[0] : throw new InvalidOperationException("Expected one mapped row.");

    private sealed record OrderLineDraft(
        int LineNumber,
        InventoryItem InventoryItem,
        InventoryUnit? InventoryUnit,
        ProductVariant Variant,
        Product Product,
        GoldProductDetail Detail,
        CalculatedProductPriceInfo Price,
        decimal GrossWeight,
        decimal NetGoldWeight,
        int Quantity);
}
