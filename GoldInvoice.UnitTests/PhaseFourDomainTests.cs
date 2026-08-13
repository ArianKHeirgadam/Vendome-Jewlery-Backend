using GoldInvoice.Application.Common;
using GoldInvoice.Application.Pricing;
using GoldInvoice.Domain.Catalog;
using GoldInvoice.Domain.Common;
using GoldInvoice.Domain.Inventory;
using GoldInvoice.Domain.Invoicing;
using GoldInvoice.Domain.Orders;
using GoldInvoice.Domain.Pricing;

namespace GoldInvoice.UnitTests;

public sealed class PhaseFourDomainTests
{
    [Fact]
    public void MarketPriceCalculation_ReturnsAuditableRialComponents()
    {
        var calculator = new ProductPriceCalculator();

        var result = calculator.Calculate(new ProductPriceCalculationInput(
            PricingMethod.MarketBased,
            MarketPriceType.Gold18K,
            FixedPriceRials: null,
            FixedGoldPricePerGramRials: null,
            ManufacturingWageType.PerGramRials,
            WageValue: 100_000m,
            ProfitPercentage: 10m,
            TaxPercentage: 10m,
            GrossWeight: 2m,
            NetGoldWeight: 2m,
            Karat: 18,
            MarketSellPriceRials: 4_000_000));

        Assert.Equal(8_000_000, result.GoldValueRials);
        Assert.Equal(200_000, result.WageRials);
        Assert.Equal(820_000, result.ProfitRials);
        Assert.Equal(102_000, result.TaxRials);
        Assert.Equal(9_122_000, result.FinalPriceRials);
        Assert.Equal(ProductPriceCalculator.RoundingPolicyName, result.RoundingPolicy);
    }

    [Fact]
    public void FixedPriceCalculation_DoesNotApplyASecondMarkup()
    {
        var result = new ProductPriceCalculator().Calculate(new ProductPriceCalculationInput(
            PricingMethod.FixedPrice,
            MarketPriceType: null,
            FixedPriceRials: 15_000_000,
            FixedGoldPricePerGramRials: null,
            ManufacturingWageType.FixedRials,
            WageValue: 500_000,
            ProfitPercentage: 12,
            TaxPercentage: 10,
            GrossWeight: 2,
            NetGoldWeight: 1.8m,
            Karat: 18,
            MarketSellPriceRials: null));

        Assert.Equal(15_000_000, result.FinalPriceRials);
        Assert.Equal(0, result.WageRials);
        Assert.Equal(0, result.ProfitRials);
        Assert.Equal(0, result.TaxRials);
    }

    [Fact]
    public void ManualReviewRule_CannotProduceAnAutomaticPrice()
    {
        Assert.Throws<ManualPriceReviewRequiredException>(() =>
            new ProductPriceCalculator().Calculate(new ProductPriceCalculationInput(
                PricingMethod.ManualReview,
                null,
                null,
                null,
                ManufacturingWageType.FixedRials,
                0,
                0,
                0,
                1,
                1,
                18,
                null)));
    }

    [Fact]
    public void GoldDetail_RejectsInconsistentComponentWeights()
    {
        Assert.Throws<ArgumentException>(() => new GoldProductDetail(
            Guid.NewGuid(),
            18,
            grossWeight: 2m,
            netGoldWeight: 1.8m,
            stoneWeight: 0.3m,
            otherMaterialWeight: 0,
            ManufacturingWageType.FixedRials,
            manufacturingWageValue: 0,
            profitPercentage: 0,
            taxPercentage: 0,
            hasStone: true,
            isWeightVariable: false));
    }

    [Fact]
    public void GoldDetail_StoresRialWageAndPercentageWageInSeparateTypedValues()
    {
        var rialDetail = CreateGoldDetail(ManufacturingWageType.FixedRials, 250_000m);
        var percentageDetail = CreateGoldDetail(
            ManufacturingWageType.PercentageOfGoldValue,
            7.5m);

        Assert.Equal(250_000, rialDetail.ManufacturingWageAmountRials);
        Assert.Null(rialDetail.ManufacturingWagePercentage);
        Assert.Null(percentageDetail.ManufacturingWageAmountRials);
        Assert.Equal(7.5m, percentageDetail.ManufacturingWagePercentage);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CreateGoldDetail(ManufacturingWageType.PercentageOfGoldValue, 100.01m));
    }

    [Fact]
    public void InventoryItem_RejectsOversellingReservedStock()
    {
        var item = new InventoryItem(Guid.NewGuid(), Guid.NewGuid(), quantityOnHand: 3);
        item.Reserve(2);

        Assert.Throws<DomainConflictException>(() => item.Reserve(2));
        Assert.Throws<DomainConflictException>(() => item.Adjust(-2));
        Assert.Equal(1, item.QuantityAvailable);
    }

    [Fact]
    public void SupplierPurchases_UseWeightedAverageAndMarkCostAsKnown()
    {
        var item = new InventoryItem(Guid.NewGuid(), Guid.NewGuid());

        item.ReceivePurchase(2, 10_000_000);
        item.Adjust(-1);
        item.ReceivePurchase(1, 20_000_000);

        Assert.True(item.HasAcquisitionCost);
        Assert.Equal(15_000_000L, item.AverageUnitCostRials);
        Assert.Equal(2, item.QuantityOnHand);
    }

    [Fact]
    public void FirstDocumentedPurchase_DoesNotTreatLegacyStockAsFree()
    {
        var item = new InventoryItem(Guid.NewGuid(), Guid.NewGuid(), quantityOnHand: 3);

        item.ReceivePurchase(1, 12_000_000);

        Assert.True(item.HasAcquisitionCost);
        Assert.Equal(12_000_000L, item.AverageUnitCostRials);
    }

    [Fact]
    public void OrderAndInvoice_PreserveActualAcquisitionProfitSnapshot()
    {
        var orderItem = new OrderItem(
            Guid.NewGuid(), Guid.NewGuid(), 1, "SKU-1", "انگشتر", "مدل یک",
            2m, 750, 18_000_000, 2, acquisitionUnitCostRials: 11_000_000);
        var invoiceItem = new InvoiceItem(
            Guid.NewGuid(), 1, "SKU-1", "انگشتر", "مدل یک",
            2m, 750, 18_000_000, 2, acquisitionUnitCostRials: 11_000_000);

        Assert.Equal(22_000_000L, orderItem.AcquisitionTotalCostRials);
        Assert.Equal(14_000_000L, orderItem.GrossProfitRials);
        Assert.Equal(orderItem.AcquisitionUnitCostRials, invoiceItem.AcquisitionUnitCostRials);
        Assert.Equal(orderItem.GrossProfitRials, invoiceItem.GrossProfitRials);
    }

    [Fact]
    public void InventoryUnit_CannotBeSoldTwice()
    {
        var unit = new InventoryUnit(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "SERIAL-1",
            "BARCODE-1",
            2m,
            1.8m,
            18,
            10_000_000,
            DateTimeOffset.Parse("2026-07-31T12:00:00+00:00"));
        unit.Reserve();
        unit.Sell(DateTimeOffset.Parse("2026-07-31T13:00:00+00:00"));

        Assert.Throws<DomainConflictException>(() =>
            unit.Sell(DateTimeOffset.Parse("2026-07-31T14:00:00+00:00")));
    }

    [Fact]
    public void ExpiredReservation_CannotBeConfirmed()
    {
        var expiresAt = DateTimeOffset.Parse("2026-07-31T12:15:00+00:00");
        var reservation = new StockReservation(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "reservation-expired",
            1,
            expiresAt);

        Assert.Throws<DomainConflictException>(() => reservation.Confirm(expiresAt));
    }

    private static GoldProductDetail CreateGoldDetail(
        ManufacturingWageType wageType,
        decimal wageValue) =>
        new(
            Guid.NewGuid(),
            18,
            grossWeight: 2m,
            netGoldWeight: 2m,
            stoneWeight: 0,
            otherMaterialWeight: 0,
            wageType,
            wageValue,
            profitPercentage: 0,
            taxPercentage: 0,
            hasStone: false,
            isWeightVariable: false);
}
