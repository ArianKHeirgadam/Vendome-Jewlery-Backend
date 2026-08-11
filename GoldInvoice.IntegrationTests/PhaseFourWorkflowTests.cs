using GoldInvoice.Application.Catalog;
using GoldInvoice.Application.Common;
using GoldInvoice.Application.Inventory;
using GoldInvoice.Application.Pricing;
using GoldInvoice.Domain.Catalog;
using GoldInvoice.Domain.Inventory;
using GoldInvoice.Domain.Orders;
using GoldInvoice.Domain.Pricing;
using GoldInvoice.Infrastructure.Catalog;
using GoldInvoice.Infrastructure.Configuration;
using GoldInvoice.Infrastructure.Inventory;
using GoldInvoice.Infrastructure.Persistence;
using GoldInvoice.Infrastructure.Persistence.Interceptors;
using GoldInvoice.Infrastructure.Pricing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GoldInvoice.IntegrationTests;

public sealed class PhaseFourWorkflowTests
{
    private static readonly DateTimeOffset FixedNow =
        DateTimeOffset.Parse("2026-07-31T20:00:00+00:00");

    [Fact]
    public async Task Catalog_CreatesGoldVariantAndRejectsCircularCategoryHierarchy()
    {
        await using var context = CreateContext();
        var service = new CatalogService(context);
        var root = await service.CreateCategoryAsync(
            new CreateProductCategoryCommand("Jewelry", "jewelry", null, 0),
            CancellationToken.None);
        var child = await service.CreateCategoryAsync(
            new CreateProductCategoryCommand("Rings", "rings", root.Id, 0),
            CancellationToken.None);
        var product = await service.CreateProductAsync(
            new CreateProductCommand(child.Id, "Classic Ring", "classic-ring", "18K ring"),
            CancellationToken.None);
        var variant = await service.CreateVariantAsync(
            product.Id,
            new CreateProductVariantCommand(
                "RING-001",
                "Size 52",
                GoldDetail(isVariable: false)),
            CancellationToken.None);

        Assert.Equal(18, Assert.IsType<GoldProductDetailInfo>(variant.GoldDetail).Karat);
        Assert.Equal(750, (await context.ProductVariants.SingleAsync()).Purity);
        await Assert.ThrowsAsync<ApplicationConflictException>(() =>
            service.UpdateCategoryAsync(
                root.Id,
                new UpdateProductCategoryCommand(
                    root.Name,
                    root.Slug,
                    child.Id,
                    root.DisplayOrder,
                    true,
                    root.RowVersion),
                CancellationToken.None));
    }

    [Fact]
    public async Task Catalog_LegacyVariantRemainsReadableUntilGoldDetailIsAdded()
    {
        await using var context = CreateContext();
        var product = new Product("Legacy ring", $"legacy-ring-{Guid.NewGuid():N}");
        var variant = new ProductVariant(
            product.Id,
            $"LEGACY-{Guid.NewGuid():N}",
            "Legacy variant",
            2m,
            750,
            100_000);
        context.AddRange(product, variant);
        await context.SaveChangesAsync();
        var service = new CatalogService(context);

        var beforeUpdate = await service.GetProductAsync(product.Id, CancellationToken.None);
        Assert.Null(Assert.Single(beforeUpdate.Variants).GoldDetail);

        var updated = await service.UpdateVariantAsync(
            variant.Id,
            new UpdateProductVariantCommand(
                variant.Sku,
                variant.Name,
                true,
                GoldDetail(isVariable: false),
                Convert.ToBase64String(variant.RowVersion),
                GoldDetailRowVersion: null),
            CancellationToken.None);

        Assert.NotNull(updated.GoldDetail);
        Assert.Single(await context.GoldProductDetails.ToListAsync());
    }

    [Fact]
    public async Task Pricing_RetriesProviderRejectsOverlapsAndPersistsCalculationSnapshot()
    {
        await using var context = CreateContext();
        var timeProvider = new FixedTimeProvider(FixedNow);
        var options = Options.Create(CreateMarketOptions());
        var catalog = new CatalogService(context);
        var product = await CreateSellableProductAsync(catalog);
        var variant = Assert.Single(product.Variants);
        var pricing = new ProductPricingService(
            context,
            new ProductPriceCalculator(),
            options,
            timeProvider);
        await pricing.CreateSourceAsync(
            new CreateMarketPriceSourceCommand(
                "Test market",
                "FAKE",
                0,
                "https://example.test/prices",
                "MarketProviders:Fake"),
            CancellationToken.None);
        var provider = new FakeMarketPriceProvider(FixedNow, failuresBeforeSuccess: 1);
        var outboxWriter = new TestOutboxWriter();
        var ingestion = new MarketPriceIngestionService(
            context,
            [provider],
            options,
            outboxWriter,
            timeProvider,
            NullLogger<MarketPriceIngestionService>.Instance);

        var stored = await ingestion.PollSourceAsync("fake", CancellationToken.None);
        var duplicateStored = await ingestion.PollSourceAsync("fake", CancellationToken.None);
        Assert.Single(outboxWriter.Events);
        var rule = await pricing.CreateRuleAsync(
            new CreateProductPricingRuleCommand(
                variant.Id,
                PricingMethod.MarketBased,
                MarketPriceType.Gold18K,
                null,
                null,
                ManufacturingWageType.PerGramRials,
                100_000,
                10,
                10,
                FixedNow.AddMinutes(-1),
                null),
            CancellationToken.None);
        var calculated = await pricing.CalculateAsync(
            new CalculateProductPriceCommand(variant.Id, null, null),
            CancellationToken.None);

        Assert.Equal(1, stored);
        Assert.Equal(0, duplicateStored);
        Assert.Equal(3, provider.AttemptCount);
        Assert.Equal(rule.Id, calculated.PricingRuleId);
        Assert.True(calculated.FinalPriceRials > calculated.GoldValueRials);
        Assert.Single(await context.PriceCalculationSnapshots.ToListAsync());
        Assert.DoesNotContain(
            "provider-secret",
            (await context.MarketPriceSnapshots.SingleAsync()).RawPayloadHash,
            StringComparison.OrdinalIgnoreCase);

        await Assert.ThrowsAsync<ApplicationConflictException>(() =>
            pricing.CreateRuleAsync(
                new CreateProductPricingRuleCommand(
                    variant.Id,
                    PricingMethod.FixedPrice,
                    null,
                    12_000_000,
                    null,
                    ManufacturingWageType.FixedRials,
                    0,
                    0,
                    0,
                    FixedNow,
                    null),
                CancellationToken.None));
    }

    [Fact]
    public async Task Pricing_InvalidProviderQuoteIsAuditedButNeverSelected()
    {
        await using var context = CreateContext();
        var timeProvider = new FixedTimeProvider(FixedNow);
        var options = Options.Create(CreateMarketOptions());
        var pricing = new ProductPricingService(
            context,
            new ProductPriceCalculator(),
            options,
            timeProvider);
        await pricing.CreateSourceAsync(
            new CreateMarketPriceSourceCommand(
                "Invalid quote provider",
                "INVALID",
                0,
                null,
                "MarketProviders:Invalid"),
            CancellationToken.None);
        var provider = new StaticMarketPriceProvider(
            "INVALID",
            new MarketPriceQuote(
                MarketPriceType.Gold18K,
                -1,
                0,
                FixedNow,
                new string('B', 64)));
        var outboxWriter = new TestOutboxWriter();
        var ingestion = new MarketPriceIngestionService(
            context,
            [provider],
            options,
            outboxWriter,
            timeProvider,
            NullLogger<MarketPriceIngestionService>.Instance);

        Assert.Equal(1, await ingestion.PollSourceAsync("INVALID", CancellationToken.None));
        var snapshot = await context.MarketPriceSnapshots.SingleAsync();
        Assert.False(snapshot.IsValid);
        Assert.Equal(MarketPriceValidationStatus.NonPositive, snapshot.ValidationStatus);
        Assert.Equal(0, snapshot.BuyPriceRials);
        Assert.Empty(outboxWriter.Events);
        await Assert.ThrowsAsync<ApplicationResourceNotFoundException>(() =>
            pricing.GetLatestMarketPriceAsync(MarketPriceType.Gold18K, CancellationToken.None));
    }

    [Fact]
    public async Task Inventory_ReservationPreventsOversellingAndWritesLedgerEntries()
    {
        await using var context = CreateContext();
        var timeProvider = new FixedTimeProvider(FixedNow);
        var catalog = new CatalogService(context);
        var product = await CreateSellableProductAsync(catalog);
        var variant = Assert.Single(product.Variants);
        var warehouse = new Warehouse("MAIN", "Main warehouse");
        var order = new Order(Guid.NewGuid(), "ORDER-001", 1_000_000, 0, 0);
        context.AddRange(warehouse, order);
        await context.SaveChangesAsync();
        var inventory = new InventoryService(context, TestOutboxWriter.Instance, timeProvider);
        var item = await inventory.ReceiveStockAsync(
            new ReceiveStockCommand(warehouse.Id, variant.Id, 3, "Purchase", Guid.NewGuid(), null),
            CancellationToken.None);
        var reservation = await inventory.ReserveAsync(
            new ReserveStockCommand(
                item.Id,
                null,
                order.Id,
                "reservation-001",
                2,
                15,
                item.RowVersion),
            CancellationToken.None);

        var secondOrder = new Order(Guid.NewGuid(), "ORDER-002", 1_000_000, 0, 0);
        context.Orders.Add(secondOrder);
        await context.SaveChangesAsync();
        await Assert.ThrowsAsync<GoldInvoice.Domain.Common.DomainConflictException>(() =>
            inventory.ReserveAsync(
                new ReserveStockCommand(
                    item.Id,
                    null,
                    secondOrder.Id,
                    "reservation-002",
                    2,
                    15,
                    string.Empty),
                CancellationToken.None));

        var currentItem = await inventory.GetInventoryItemAsync(item.Id, CancellationToken.None);
        var confirmed = await inventory.ConfirmReservationAsync(
            reservation.Id,
            reservation.RowVersion,
            currentItem.RowVersion,
            CancellationToken.None);
        var finalItem = await inventory.GetInventoryItemAsync(item.Id, CancellationToken.None);

        Assert.Equal(StockReservationStatus.Confirmed, confirmed.Status);
        Assert.Equal(1, finalItem.QuantityOnHand);
        Assert.Equal(0, finalItem.QuantityReserved);
        Assert.Equal(3, await context.StockMovements.CountAsync());
        Assert.Contains(
            await context.StockMovements.ToListAsync(),
            movement => movement.MovementType == StockMovementType.ReservationConfirmed &&
                movement.QuantityDelta == -2 &&
                movement.ReservedQuantityDelta == -2);
    }

    [Fact]
    public async Task Inventory_PhysicalUnitCanBeLookedUpAndTransferredAtomically()
    {
        await using var context = CreateContext();
        var timeProvider = new FixedTimeProvider(FixedNow);
        var catalog = new CatalogService(context);
        var product = await CreateSellableProductAsync(catalog);
        var variant = Assert.Single(product.Variants);
        var sourceWarehouse = new Warehouse("SOURCE", "Source warehouse");
        var destinationWarehouse = new Warehouse("DEST", "Destination warehouse");
        context.AddRange(sourceWarehouse, destinationWarehouse);
        await context.SaveChangesAsync();
        var inventory = new InventoryService(context, TestOutboxWriter.Instance, timeProvider);

        var received = await inventory.ReceiveInventoryUnitAsync(
            new ReceiveInventoryUnitCommand(
                product.Id,
                variant.Id,
                sourceWarehouse.Id,
                "SERIAL-100",
                "BARCODE-100",
                2m,
                2m,
                18,
                8_000_000,
                FixedNow),
            CancellationToken.None);
        var found = await inventory.FindInventoryUnitAsync("barcode-100", CancellationToken.None);
        var sourceItem = await inventory.GetInventoryItemAsync(received.InventoryItemId, CancellationToken.None);
        var transferred = await inventory.TransferInventoryUnitAsync(
            new TransferInventoryUnitCommand(
                received.Id,
                destinationWarehouse.Id,
                received.RowVersion,
                sourceItem.RowVersion),
            CancellationToken.None);

        Assert.Equal(received.Id, found.Id);
        Assert.Equal(destinationWarehouse.Id, transferred.WarehouseId);
        Assert.Equal(0, (await inventory.GetInventoryItemAsync(sourceItem.Id, CancellationToken.None)).QuantityOnHand);
        Assert.Equal(1, (await inventory.GetInventoryItemAsync(transferred.InventoryItemId, CancellationToken.None)).QuantityOnHand);
        Assert.Equal(3, (await inventory.GetStockMovementsAsync(
            sourceItem.Id,
            1,
            100,
            CancellationToken.None)).TotalCount +
            (await inventory.GetStockMovementsAsync(
                transferred.InventoryItemId,
                1,
                100,
                CancellationToken.None)).TotalCount);
    }

    private static async Task<ProductInfo> CreateSellableProductAsync(CatalogService catalog)
    {
        var category = await catalog.CreateCategoryAsync(
            new CreateProductCategoryCommand("Rings", $"rings-{Guid.NewGuid():N}", null, 0),
            CancellationToken.None);
        var product = await catalog.CreateProductAsync(
            new CreateProductCommand(
                category.Id,
                "Test ring",
                $"test-ring-{Guid.NewGuid():N}",
                null),
            CancellationToken.None);
        await catalog.CreateVariantAsync(
            product.Id,
            new CreateProductVariantCommand(
                $"SKU-{Guid.NewGuid():N}",
                "Default",
                GoldDetail(isVariable: false)),
            CancellationToken.None);
        return await catalog.GetProductAsync(product.Id, CancellationToken.None);
    }

    private static GoldProductDetailCommand GoldDetail(bool isVariable) => new(
        Karat: 18,
        GrossWeight: 2m,
        NetGoldWeight: 2m,
        StoneWeight: 0,
        OtherMaterialWeight: 0,
        ManufacturingWageType.PerGramRials,
        ManufacturingWageValue: 100_000,
        ProfitPercentage: 10,
        TaxPercentage: 10,
        HasStone: false,
        IsWeightVariable: isVariable);

    private static GoldInvoiceDbContext CreateContext()
    {
        var timeProvider = new FixedTimeProvider(FixedNow);
        var options = new DbContextOptionsBuilder<GoldInvoiceDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .AddInterceptors(new AuditingSaveChangesInterceptor(timeProvider))
            .Options;
        return new GoldInvoiceDbContext(options);
    }

    private static MarketPriceOptions CreateMarketOptions() => new()
    {
        ProviderTimeoutSeconds = 2,
        RetryCount = 3,
        RetryBaseDelayMilliseconds = 10,
        MaximumQuoteAgeMinutes = 30,
        MaximumFutureClockSkewSeconds = 30,
        PollIntervalMinutes = 5
    };

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class FakeMarketPriceProvider(
        DateTimeOffset timestamp,
        int failuresBeforeSuccess) : IMarketPriceProvider
    {
        public string ProviderCode => "FAKE";

        public int AttemptCount { get; private set; }

        public Task<IReadOnlyList<MarketPriceQuote>> FetchAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AttemptCount++;
            if (AttemptCount <= failuresBeforeSuccess)
            {
                throw new HttpRequestException("Simulated provider failure.");
            }

            IReadOnlyList<MarketPriceQuote> result =
            [
                new(
                    MarketPriceType.Gold18K,
                    3_900_000,
                    4_000_000,
                    timestamp,
                    new string('A', 64))
            ];
            return Task.FromResult(result);
        }
    }

    private sealed class StaticMarketPriceProvider(
        string providerCode,
        MarketPriceQuote quote) : IMarketPriceProvider
    {
        public string ProviderCode => providerCode;

        public Task<IReadOnlyList<MarketPriceQuote>> FetchAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<MarketPriceQuote>>([quote]);
        }
    }
}
