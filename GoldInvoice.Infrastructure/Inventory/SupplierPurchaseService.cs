using GoldInvoice.Application.Common;
using GoldInvoice.Application.Integration;
using GoldInvoice.Application.Inventory;
using GoldInvoice.Domain.Business;
using GoldInvoice.Domain.Catalog;
using GoldInvoice.Domain.Inventory;
using GoldInvoice.Domain.Pricing;
using GoldInvoice.Infrastructure.Integration;
using GoldInvoice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoldInvoice.Infrastructure.Inventory;

internal sealed class SupplierPurchaseService(
    GoldInvoiceDbContext dbContext,
    IOutboxWriter outboxWriter,
    TimeProvider timeProvider) : ISupplierPurchaseService
{
    private const int MaximumPageSize = 100;

    public async Task<PagedResult<SupplierPurchaseInfo>> GetPurchasesAsync(
        int page,
        int pageSize,
        Guid? supplierId,
        CancellationToken cancellationToken)
    {
        ValidatePage(page, pageSize);
        var query =
            from purchase in dbContext.Set<SupplierPurchase>().AsNoTracking()
            join supplier in dbContext.Suppliers.AsNoTracking() on purchase.SupplierId equals supplier.Id
            join warehouse in dbContext.Warehouses.AsNoTracking() on purchase.WarehouseId equals warehouse.Id
            join variant in dbContext.ProductVariants.AsNoTracking() on purchase.ProductVariantId equals variant.Id
            join product in dbContext.Products.AsNoTracking() on variant.ProductId equals product.Id
            select new { Purchase = purchase, SupplierName = supplier.Name, WarehouseName = warehouse.Name, ProductName = product.Name, Variant = variant };
        if (supplierId is not null)
        {
            query = query.Where(item => item.Purchase.SupplierId == supplierId);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(item => item.Purchase.PurchasedAt)
            .ThenByDescending(item => item.Purchase.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<SupplierPurchaseInfo>(
            rows.Select(item => Map(
                item.Purchase,
                item.SupplierName,
                item.WarehouseName,
                item.ProductName,
                item.Variant.Name,
                item.Variant.Sku)).ToArray(),
            page,
            pageSize,
            totalCount);
    }

    public async Task<SupplierPurchaseInfo> RecordPurchaseAsync(
        RecordSupplierPurchaseCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.SupplierId == Guid.Empty ||
            command.WarehouseId == Guid.Empty ||
            command.ProductVariantId == Guid.Empty ||
            command.Quantity <= 0 ||
            command.UnitCostRials < 0 ||
            command.SellingUnitPriceRials <= 0)
        {
            throw new ArgumentException("The supplier purchase values are invalid.", nameof(command));
        }

        var now = timeProvider.GetUtcNow();
        var purchasedAt = (command.PurchasedAt ?? now).ToUniversalTime();
        if (purchasedAt > now)
        {
            throw new ArgumentOutOfRangeException(nameof(command.PurchasedAt));
        }

        await using var transaction = await BeginTransactionAsync(cancellationToken);
        var supplier = await dbContext.Suppliers.SingleOrDefaultAsync(
            item => item.Id == command.SupplierId && item.IsActive,
            cancellationToken) ?? throw new ApplicationResourceNotFoundException();
        var warehouse = await dbContext.Warehouses.SingleOrDefaultAsync(
            item => item.Id == command.WarehouseId && item.IsActive,
            cancellationToken) ?? throw new ApplicationResourceNotFoundException();
        var variantData = await (
                from variant in dbContext.ProductVariants
                join product in dbContext.Products on variant.ProductId equals product.Id
                join detail in dbContext.GoldProductDetails on variant.Id equals detail.ProductVariantId
                where variant.Id == command.ProductVariantId && variant.IsActive && product.IsActive
                select new { Variant = variant, ProductName = product.Name })
            .SingleOrDefaultAsync(cancellationToken) ?? throw new ApplicationResourceNotFoundException();

        var item = await dbContext.InventoryItems.SingleOrDefaultAsync(
            candidate => candidate.WarehouseId == warehouse.Id &&
                candidate.ProductVariantId == variantData.Variant.Id,
            cancellationToken);
        if (item is null)
        {
            item = new InventoryItem(warehouse.Id, variantData.Variant.Id);
            dbContext.InventoryItems.Add(item);
        }

        item.ReceivePurchase(command.Quantity, command.UnitCostRials);
        foreach (var currentRule in await dbContext.ProductPricingRules
                     .Where(rule => rule.ProductVariantId == variantData.Variant.Id && rule.IsActive)
                     .ToListAsync(cancellationToken))
        {
            currentRule.Deactivate();
        }

        var pricingRule = new ProductPricingRule(
            variantData.Variant.Id,
            PricingMethod.FixedPrice,
            goldMarketPriceType: null,
            fixedPriceRials: command.SellingUnitPriceRials,
            fixedGoldPricePerGramRials: null,
            ManufacturingWageType.FixedRials,
            wageValue: 0,
            profitPercentage: 0,
            taxPercentage: 0,
            effectiveFrom: now);
        dbContext.ProductPricingRules.Add(pricingRule);

        var movement = new StockMovement(
            item.Id,
            StockMovementType.Purchase,
            command.Quantity,
            item.QuantityOnHand,
            now,
            reservedQuantityDelta: 0,
            reservedBalanceAfter: item.QuantityReserved);
        var purchase = new SupplierPurchase(
            CreatePurchaseNumber(now),
            supplier.Id,
            warehouse.Id,
            variantData.Variant.Id,
            item.Id,
            movement.Id,
            pricingRule.Id,
            command.Quantity,
            command.UnitCostRials,
            command.SellingUnitPriceRials,
            purchasedAt,
            command.SupplierReference,
            command.Notes);
        movement.SetReference("SupplierPurchase", purchase.Id, command.Notes);
        dbContext.StockMovements.Add(movement);
        dbContext.Set<SupplierPurchase>().Add(purchase);
        outboxWriter.AddInventoryChanged(item, movement);

        await SaveChangesAsync(cancellationToken);
        await CommitAsync(transaction, cancellationToken);
        return Map(
            purchase,
            supplier.Name,
            warehouse.Name,
            variantData.ProductName,
            variantData.Variant.Name,
            variantData.Variant.Sku);
    }

    private static SupplierPurchaseInfo Map(
        SupplierPurchase purchase,
        string supplierName,
        string warehouseName,
        string productName,
        string variantName,
        string sku) => new(
            purchase.Id,
            purchase.PurchaseNumber,
            purchase.SupplierId,
            supplierName,
            purchase.WarehouseId,
            warehouseName,
            purchase.ProductVariantId,
            productName,
            variantName,
            sku,
            purchase.InventoryItemId,
            purchase.Quantity,
            purchase.UnitCostRials,
            purchase.TotalCostRials,
            purchase.SellingUnitPriceRials,
            purchase.ExpectedUnitProfitRials,
            purchase.ExpectedTotalProfitRials,
            purchase.PurchasedAt,
            purchase.SupplierReference,
            purchase.Notes);

    private static string CreatePurchaseNumber(DateTimeOffset now) =>
        $"PUR-{now:yyyyMMdd}-{Guid.NewGuid():N}".ToUpperInvariant();

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
            ? await dbContext.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable,
                cancellationToken)
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

    private static void ValidatePage(int page, int pageSize)
    {
        if (page < 1 || pageSize is < 1 or > MaximumPageSize || ((long)page - 1) * pageSize > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(page));
        }
    }
}
