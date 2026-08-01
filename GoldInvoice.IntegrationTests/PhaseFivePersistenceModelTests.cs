using GoldInvoice.Domain.Customers;
using GoldInvoice.Domain.Invoicing;
using GoldInvoice.Domain.Inventory;
using GoldInvoice.Domain.Orders;
using GoldInvoice.Domain.Payments;
using GoldInvoice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace GoldInvoice.IntegrationTests;

public sealed class PhaseFivePersistenceModelTests
{
    [Fact]
    public void Model_ContainsEveryPhaseFiveEntityInTheExpectedSchema()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;

        AssertEntity(model, typeof(CustomerAddress), "CustomerAddresses", "sales");
        AssertEntity(model, typeof(OrderStoreSnapshot), "OrderStoreSnapshots", "sales");
        AssertEntity(model, typeof(PaymentGateway), "PaymentGateways", "billing");
        AssertEntity(model, typeof(InvoiceSequence), "InvoiceSequences", "invoicing");
        AssertEntity(model, typeof(InvoiceAddressSnapshot), "InvoiceAddressSnapshots", "invoicing");
        AssertEntity(model, typeof(InvoiceStoreSnapshot), "InvoiceStoreSnapshots", "invoicing");
    }

    [Fact]
    public void PhaseFiveRelationships_NeverCascadeDelete()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;
        Type[] types =
        [
            typeof(CustomerAddress),
            typeof(OrderItem),
            typeof(OrderAddressSnapshot),
            typeof(OrderStoreSnapshot),
            typeof(Payment),
            typeof(PaymentGateway),
            typeof(Invoice),
            typeof(InvoiceItem),
            typeof(InvoiceAddressSnapshot),
            typeof(InvoiceStoreSnapshot),
            typeof(StockReservation)
        ];

        foreach (var type in types)
        {
            Assert.All(
                model.FindEntityType(type)!.GetForeignKeys(),
                foreignKey => Assert.Contains(
                    foreignKey.DeleteBehavior,
                    new[] { DeleteBehavior.Restrict, DeleteBehavior.NoAction }));
        }
    }

    [Fact]
    public void PhaseFiveIndexes_ProtectDefaultsCallbacksPaymentsAndInvoiceNumbers()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;
        var address = model.FindEntityType(typeof(CustomerAddress))!;
        var payment = model.FindEntityType(typeof(Payment))!;
        var callback = model.FindEntityType(typeof(PaymentCallback))!;
        var invoice = model.FindEntityType(typeof(Invoice))!;
        var sequence = model.FindEntityType(typeof(InvoiceSequence))!;
        var reservation = model.FindEntityType(typeof(StockReservation))!;
        var orderItem = model.FindEntityType(typeof(OrderItem))!;

        Assert.Contains(address.GetIndexes(), index =>
            index.IsUnique && index.Properties.Single().Name == nameof(CustomerAddress.CustomerId));
        Assert.Contains(payment.GetIndexes(), index =>
            index.IsUnique && index.Properties.Single().Name == nameof(Payment.IdempotencyKeyHash));
        Assert.Contains(payment.GetIndexes(), index =>
            index.IsUnique &&
            index.Properties.Single().Name == nameof(Payment.OrderId) &&
            index.GetFilter()!.Contains("RequiresReview", StringComparison.Ordinal));
        Assert.Contains(callback.GetIndexes(), index =>
            index.IsUnique && index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(PaymentCallback.Provider), nameof(PaymentCallback.ExternalCallbackId)]));
        Assert.Contains(invoice.GetIndexes(), index =>
            index.IsUnique && index.Properties.Single().Name == nameof(Invoice.OrderId));
        Assert.Contains(invoice.GetIndexes(), index =>
            index.IsUnique && index.Properties.Single().Name == nameof(Invoice.PaymentId));
        Assert.Contains(invoice.GetIndexes(), index =>
            index.IsUnique && index.Properties.Single().Name == nameof(Invoice.InvoiceNumber));
        Assert.Contains(sequence.GetIndexes(), index =>
            index.IsUnique && index.Properties.Single().Name == nameof(InvoiceSequence.Series));
        Assert.Contains(sequence.GetIndexes(), index =>
            index.IsUnique && index.Properties.Single().Name == nameof(InvoiceSequence.Prefix));
        Assert.Contains(reservation.GetIndexes(), index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(StockReservation.OrderId), nameof(StockReservation.InventoryItemId)]) &&
            index.GetFilter()!.Contains("InventoryUnitId] IS NULL", StringComparison.Ordinal));
        Assert.Contains(orderItem.GetIndexes(), index =>
            !index.IsUnique &&
            index.Properties.Single().Name == nameof(OrderItem.InventoryUnitId));
    }

    [Fact]
    public void PhaseFiveFinancialRowsUseRialsAndSqlServerConcurrencyTokens()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;
        var orderItem = model.FindEntityType(typeof(OrderItem))!;
        var invoiceItem = model.FindEntityType(typeof(InvoiceItem))!;
        var payment = model.FindEntityType(typeof(Payment))!;

        Assert.Equal("bigint", orderItem.FindProperty(nameof(OrderItem.GoldValueRials))!.GetColumnType());
        Assert.Equal("bigint", invoiceItem.FindProperty(nameof(InvoiceItem.TaxRials))!.GetColumnType());
        Assert.Equal("bigint", payment.FindProperty(nameof(Payment.AmountRials))!.GetColumnType());
        Assert.True(orderItem.FindProperty(nameof(OrderItem.RowVersion))!.IsConcurrencyToken);
        Assert.True(payment.FindProperty(nameof(Payment.RowVersion))!.IsConcurrencyToken);
    }

    [Fact]
    public void Database_HasAdditivePhaseFiveMigrationAndNoPendingModelChanges()
    {
        using var context = CreateContext();

        Assert.Contains(
            context.Database.GetMigrations(),
            migration => migration.EndsWith("_AddPhase5OrdersPaymentsInvoices", StringComparison.Ordinal));
        Assert.False(context.Database.HasPendingModelChanges());
    }

    private static void AssertEntity(IModel model, Type type, string table, string schema)
    {
        var entity = model.FindEntityType(type);
        Assert.NotNull(entity);
        Assert.Equal(table, entity.GetTableName());
        Assert.Equal(schema, entity.GetSchema());
    }

    private static GoldInvoiceDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<GoldInvoiceDbContext>()
            .UseSqlServer(
                "Server=localhost;Database=GoldInvoicePhaseFiveModelTests;Integrated Security=True;Encrypt=True;TrustServerCertificate=True")
            .Options;
        return new GoldInvoiceDbContext(options);
    }
}
