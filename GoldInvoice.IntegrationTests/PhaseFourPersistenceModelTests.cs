using GoldInvoice.Domain.Catalog;
using GoldInvoice.Domain.Inventory;
using GoldInvoice.Domain.Pricing;
using GoldInvoice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace GoldInvoice.IntegrationTests;

public sealed class PhaseFourPersistenceModelTests
{
    [Fact]
    public void Model_ContainsEveryPhaseFourEntityInTheExpectedSchema()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;

        AssertEntity(model, typeof(ProductCategory), "ProductCategories", "catalog");
        AssertEntity(model, typeof(GoldProductDetail), "GoldProductDetails", "catalog");
        AssertEntity(model, typeof(ProductPricingRule), "ProductPricingRules", "pricing");
        AssertEntity(model, typeof(MarketPriceSource), "MarketPriceSources", "pricing");
        AssertEntity(model, typeof(MarketPriceSnapshot), "MarketPriceSnapshots", "pricing");
        AssertEntity(model, typeof(PriceCalculationSnapshot), "PriceCalculationSnapshots", "pricing");
        AssertEntity(model, typeof(InventoryUnit), "InventoryUnits", "inventory");
    }

    [Fact]
    public void PhaseFourRelationships_NeverCascadeDelete()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;
        Type[] phaseFourTypes =
        [
            typeof(ProductCategory),
            typeof(GoldProductDetail),
            typeof(ProductPricingRule),
            typeof(MarketPriceSnapshot),
            typeof(PriceCalculationSnapshot),
            typeof(InventoryUnit),
            typeof(StockMovement),
            typeof(StockReservation)
        ];

        foreach (var type in phaseFourTypes)
        {
            Assert.All(
                model.FindEntityType(type)!.GetForeignKeys(),
                foreignKey => Assert.Contains(
                    foreignKey.DeleteBehavior,
                    new[] { DeleteBehavior.Restrict, DeleteBehavior.NoAction }));
        }
    }

    [Fact]
    public void PhaseFourIndexes_ProtectSlugsPhysicalIdentifiersAndMarketDuplicates()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;
        var category = model.FindEntityType(typeof(ProductCategory))!;
        var unit = model.FindEntityType(typeof(InventoryUnit))!;
        var market = model.FindEntityType(typeof(MarketPriceSnapshot))!;

        Assert.Contains(category.GetIndexes(), index =>
            index.IsUnique && index.Properties.Single().Name == nameof(ProductCategory.Slug));
        Assert.Contains(unit.GetIndexes(), index =>
            index.IsUnique && index.Properties.Single().Name == nameof(InventoryUnit.SerialNumber));
        Assert.Contains(unit.GetIndexes(), index =>
            index.IsUnique && index.Properties.Single().Name == nameof(InventoryUnit.Barcode));
        Assert.Contains(market.GetIndexes(), index =>
            index.IsUnique && index.Properties.Select(property => property.Name)
                .SequenceEqual([
                    nameof(MarketPriceSnapshot.SourceId),
                    nameof(MarketPriceSnapshot.PriceType),
                    nameof(MarketPriceSnapshot.CapturedAt)]));
    }

    [Fact]
    public void PhaseFourRows_HaveSqlServerConcurrencyAndExactNumericTypes()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;
        var unit = model.FindEntityType(typeof(InventoryUnit))!;
        var detail = model.FindEntityType(typeof(GoldProductDetail))!;
        var rule = model.FindEntityType(typeof(ProductPricingRule))!;

        Assert.True(unit.FindProperty(nameof(InventoryUnit.RowVersion))!.IsConcurrencyToken);
        Assert.Equal("decimal(18,3)", detail.FindProperty(nameof(GoldProductDetail.NetGoldWeight))!.GetColumnType());
        Assert.Equal(
            "bigint",
            detail.FindProperty(nameof(GoldProductDetail.ManufacturingWageAmountRials))!.GetColumnType());
        Assert.Null(detail.FindProperty(nameof(GoldProductDetail.ManufacturingWageValue)));
        Assert.Equal("bigint", rule.FindProperty(nameof(ProductPricingRule.FixedPriceRials))!.GetColumnType());
        Assert.Equal("bigint", rule.FindProperty(nameof(ProductPricingRule.WageAmountRials))!.GetColumnType());
        Assert.Null(rule.FindProperty(nameof(ProductPricingRule.WageValue)));
        Assert.Equal("decimal(9,4)", rule.FindProperty(nameof(ProductPricingRule.ProfitPercentage))!.GetColumnType());
    }

    [Fact]
    public void Database_HasAdditivePhaseFourMigration()
    {
        using var context = CreateContext();

        Assert.Contains(
            context.Database.GetMigrations(),
            migration => migration.EndsWith(
                "_AddPhase4CatalogPricingInventory",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Database_ModelMatchesTheLatestMigrationSnapshot()
    {
        using var context = CreateContext();

        Assert.False(context.Database.HasPendingModelChanges());
    }

    private static void AssertEntity(
        IModel model,
        Type clrType,
        string tableName,
        string schema)
    {
        var entity = model.FindEntityType(clrType);
        Assert.NotNull(entity);
        Assert.Equal(tableName, entity.GetTableName());
        Assert.Equal(schema, entity.GetSchema());
    }

    private static GoldInvoiceDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<GoldInvoiceDbContext>()
            .UseSqlServer(
                "Server=localhost;Database=GoldInvoicePhaseFourModelTests;Integrated Security=True;Encrypt=True;TrustServerCertificate=True")
            .Options;
        return new GoldInvoiceDbContext(options);
    }
}
