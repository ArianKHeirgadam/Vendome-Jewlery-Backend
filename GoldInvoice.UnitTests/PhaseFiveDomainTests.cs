using GoldInvoice.Domain.Customers;
using GoldInvoice.Domain.Invoicing;
using GoldInvoice.Domain.Orders;
using GoldInvoice.Domain.Payments;

namespace GoldInvoice.UnitTests;

public sealed class PhaseFiveDomainTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-08-01T12:00:00+00:00");

    [Fact]
    public void OrderItem_PreservesCompletePriceAndGoldSnapshot()
    {
        var item = new OrderItem(
            Guid.NewGuid(),
            Guid.NewGuid(),
            1,
            "RING-001",
            "Classic ring",
            "Size 52",
            2m,
            750,
            9_122_000,
            1,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            2m,
            18,
            4_000_000,
            8_000_000,
            200_000,
            820_000,
            102_000,
            "WholeRialAwayFromZero");

        Assert.Equal(18, item.Karat);
        Assert.Equal(8_000_000, item.GoldValueRials);
        Assert.Equal(item.UnitPriceRials, item.LineTotalRials);
    }

    [Fact]
    public void OrderItem_RejectsAComponentTotalThatDoesNotMatchItsUnitPrice()
    {
        Assert.Throws<ArgumentException>(() => new OrderItem(
            Guid.NewGuid(),
            Guid.NewGuid(),
            1,
            "RING-001",
            "Classic ring",
            "Size 52",
            2m,
            750,
            9_122_001,
            1,
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            2m,
            18,
            4_000_000,
            8_000_000,
            200_000,
            820_000,
            102_000,
            "WholeRialAwayFromZero"));
    }

    [Fact]
    public void OrderItem_RejectsAPartialPhaseFiveSnapshot()
    {
        Assert.Throws<ArgumentException>(() => new OrderItem(
            Guid.NewGuid(),
            Guid.NewGuid(),
            1,
            "RING-001",
            "Classic ring",
            "Size 52",
            2m,
            750,
            9_122_000,
            1,
            priceCalculationSnapshotId: null,
            inventoryItemId: Guid.NewGuid()));
    }

    [Fact]
    public void Order_PaymentControlledTransitionsCannotBeSkipped()
    {
        var order = new Order(Guid.NewGuid(), "ORDER-1", 10_000_000, 0, 0);

        Assert.Throws<GoldInvoice.Domain.Common.DomainConflictException>(() => order.MarkPaid(Now));
        order.MoveToAwaitingPayment();
        order.MarkPaymentReview();
        order.MarkPaid(Now);
        order.MoveToProcessing();
        order.Complete();

        Assert.Equal(OrderStatus.Completed, order.Status);
    }

    [Fact]
    public void Order_RejectsAZeroGrandTotalWithoutAFreeOrderWorkflow()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Order(Guid.NewGuid(), "ORDER-FREE", 10_000_000, 10_000_000, 0));
    }

    [Fact]
    public void InvoiceSequence_AllocatesMonotonicUniqueNumbers()
    {
        var sequence = new InvoiceSequence("default", "inv");

        var numbers = Enumerable.Range(0, 250)
            .Select(_ => sequence.AllocateNext(Now))
            .ToArray();

        Assert.Equal(250, numbers.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal("INV-0000000001", numbers[0]);
        Assert.Equal("INV-0000000250", numbers[^1]);
        Assert.Equal(251, sequence.NextValue);
    }

    [Fact]
    public void Invoice_CanOnlyBeVoidedOnceWithAReason()
    {
        var invoice = new Invoice(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "INV-1",
            Now,
            10_000_000,
            0,
            0,
            Guid.NewGuid());

        invoice.Void(Now.AddMinutes(1), "Duplicate fiscal document");

        Assert.Equal(InvoiceStatus.Voided, invoice.Status);
        Assert.Throws<GoldInvoice.Domain.Common.DomainConflictException>(() =>
            invoice.Void(Now.AddMinutes(2), "Second void"));
    }

    [Fact]
    public void Payment_SeparatesOnlineAndManualGatewayReferences()
    {
        var online = new Payment(
            Guid.NewGuid(),
            "FAKE",
            10_000_000,
            PaymentMethod.OnlineGateway,
            Guid.NewGuid(),
            new string('A', 64));
        online.BeginProcessing("AUTH-1");
        online.Verify("PAY-1", Now);

        Assert.Equal(PaymentStatus.Verified, online.Status);
        Assert.Throws<ArgumentException>(() => new Payment(
            Guid.NewGuid(),
            "MANUAL",
            10_000_000,
            PaymentMethod.Cash,
            Guid.NewGuid()));
    }

    [Fact]
    public void CustomerAddress_NormalizesSnapshotSourceFields()
    {
        var address = new CustomerAddress(
            Guid.NewGuid(),
            " Home ",
            " Arian ",
            "09120000000",
            "East Azerbaijan",
            "Tabriz",
            "1234567890",
            "Main street",
            isDefault: true);

        Assert.Equal("Home", address.Title);
        Assert.Equal("Arian", address.RecipientName);
        Assert.True(address.IsDefault);
    }
}
