using GoldInvoice.Domain.Catalog;
using GoldInvoice.Domain.Inventory;
using GoldInvoice.Domain.Invoicing;
using GoldInvoice.Domain.Orders;
using GoldInvoice.Domain.Platform;

namespace GoldInvoice.UnitTests;

public sealed class DomainModelTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1001)]
    public void ProductVariant_WithInvalidPurity_Throws(int purity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ProductVariant(Guid.NewGuid(), "SKU-1", "Variant", 1.25m, purity, 0));
    }

    [Fact]
    public void InventoryItem_WithNegativeStock_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new InventoryItem(Guid.NewGuid(), Guid.NewGuid(), -1));
    }

    [Fact]
    public void Order_ComputesGrandTotalInRials()
    {
        var order = new Order(Guid.NewGuid(), "order-1", 10_000_000, 500_000, 100_000);

        Assert.Equal(9_600_000, order.GrandTotalRials);
    }

    [Fact]
    public void Order_WithDiscountAboveSubtotal_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Order(Guid.NewGuid(), "order-1", 100, 101, 0));
    }

    [Fact]
    public void Invoice_PreservesSnapshotTotals()
    {
        var invoice = new Invoice(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "invoice-1",
            DateTimeOffset.Parse("2026-07-31T12:00:00+00:00"),
            20_000_000,
            1_000_000,
            250_000);

        Assert.Equal(19_250_000, invoice.GrandTotalRials);
    }

    [Fact]
    public void SystemSetting_RequiresExactlyOneValueSource()
    {
        Assert.Throws<ArgumentException>(() =>
            new SystemSetting("Payments.Secret", "string", "plain", "vault://payments"));
        Assert.Throws<ArgumentException>(() =>
            new SystemSetting("Payments.Secret", "string", null, null));
    }
}
