using GoldInvoice.Application;
using GoldInvoice.Domain.Business;
using GoldInvoice.Domain.Catalog;
using GoldInvoice.Domain.Inventory;
using GoldInvoice.Domain.Invoicing;
using GoldInvoice.Domain.Orders;
using GoldInvoice.Domain.Payments;
using GoldInvoice.Domain.Platform;
using GoldInvoice.Domain.Security;
using GoldInvoice.Infrastructure;
using GoldInvoice.Infrastructure.Configuration;
using GoldInvoice.Infrastructure.Identity;
using GoldInvoice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace GoldInvoice.IntegrationTests;

public sealed class PersistenceModelTests
{
    [Fact]
    public void Model_ContainsEveryPhaseTwoEntity()
    {
        using var context = CreateContext();
        Type[] expectedTypes =
        [
            typeof(ApplicationUser),
            typeof(ApplicationRole),
            typeof(Permission),
            typeof(RolePermission),
            typeof(RefreshToken),
            typeof(UserSession),
            typeof(TrustedDevice),
            typeof(LoginAttempt),
            typeof(SecurityEvent),
            typeof(Product),
            typeof(ProductVariant),
            typeof(ProductImage),
            typeof(SupplierPurchase),
            typeof(Warehouse),
            typeof(InventoryItem),
            typeof(StockMovement),
            typeof(StockReservation),
            typeof(InventoryAdjustment),
            typeof(Order),
            typeof(OrderItem),
            typeof(OrderStatusHistory),
            typeof(OrderAddressSnapshot),
            typeof(Payment),
            typeof(PaymentAttempt),
            typeof(PaymentCallback),
            typeof(Invoice),
            typeof(InvoiceItem),
            typeof(InvoicePrintLog),
            typeof(DesktopDevice),
            typeof(OutboxMessage),
            typeof(AuditLog),
            typeof(SystemSetting),
            typeof(IdempotencyRecord)
        ];

        Assert.All(expectedTypes, type => Assert.NotNull(context.Model.FindEntityType(type)));
    }

    [Fact]
    public void InventoryItem_HasUniqueLocationAndVariantIndexAndRowVersion()
    {
        using var context = CreateContext();
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(InventoryItem))!;

        Assert.Contains(
            entity.GetIndexes(),
            index => index.IsUnique &&
                index.Properties.Select(property => property.Name)
                    .SequenceEqual([nameof(InventoryItem.WarehouseId), nameof(InventoryItem.ProductVariantId)]));
        var rowVersion = entity.FindProperty(nameof(InventoryItem.RowVersion))!;
        Assert.True(rowVersion.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, rowVersion.ValueGenerated);
        Assert.Contains(
            entity.GetCheckConstraints(),
            constraint => constraint.Name == "CK_InventoryItems_Available");
    }

    [Fact]
    public void FinancialRelationships_NeverCascadeDelete()
    {
        using var context = CreateContext();
        Type[] financialTypes =
        [
            typeof(Order),
            typeof(OrderItem),
            typeof(Payment),
            typeof(PaymentAttempt),
            typeof(PaymentCallback),
            typeof(Invoice),
            typeof(InvoiceItem),
            typeof(InvoicePrintLog)
        ];

        foreach (var type in financialTypes)
        {
            var entity = context.Model.FindEntityType(type)!;
            Assert.All(entity.GetForeignKeys(), foreignKey =>
                Assert.Equal(DeleteBehavior.NoAction, foreignKey.DeleteBehavior));
        }
    }

    [Fact]
    public void MoneyColumns_AreBigintAndSnapshotRowsHaveUniqueNumbers()
    {
        using var context = CreateContext();
        var payment = context.Model.FindEntityType(typeof(Payment))!;
        var invoice = context.Model.FindEntityType(typeof(Invoice))!;
        var invoiceItem = context.Model.FindEntityType(typeof(InvoiceItem))!;

        Assert.Equal("bigint", payment.FindProperty(nameof(Payment.AmountRials))!.GetColumnType());
        Assert.Contains(
            invoice.GetIndexes(),
            index => index.IsUnique && index.Properties.Single().Name == nameof(Invoice.InvoiceNumber));
        Assert.Contains(
            invoiceItem.GetIndexes(),
            index => index.IsUnique &&
                index.Properties.Select(property => property.Name)
                    .SequenceEqual([nameof(InvoiceItem.InvoiceId), nameof(InvoiceItem.LineNumber)]));
    }

    [Fact]
    public void SoftDeletedCatalogRows_AreFilteredByDefault()
    {
        using var context = CreateContext();

        Assert.NotNull(context.Model.FindEntityType(typeof(Product))!.GetQueryFilter());
        Assert.NotNull(context.Model.FindEntityType(typeof(ProductVariant))!.GetQueryFilter());
        Assert.NotNull(context.Model.FindEntityType(typeof(ProductImage))!.GetQueryFilter());
    }

    [Fact]
    public void ProductImageVariantRelationship_RequiresTheSameProduct()
    {
        using var context = CreateContext();
        var image = context.Model.FindEntityType(typeof(ProductImage))!;

        Assert.Contains(
            image.GetForeignKeys(),
            foreignKey =>
                foreignKey.PrincipalEntityType.ClrType == typeof(ProductVariant) &&
                foreignKey.Properties.Select(property => property.Name)
                    .SequenceEqual([nameof(ProductImage.ProductId), nameof(ProductImage.ProductVariantId)]));
    }

    [Fact]
    public void Database_HasInitialDomainModelMigration()
    {
        using var context = CreateContext();

        Assert.Contains(
            context.Database.GetMigrations(),
            migration => migration.EndsWith("_InitialDomainModel", StringComparison.Ordinal));
    }

    [Fact]
    public void AddInfrastructure_RegistersSqlServerContextAndReadinessCheck()
    {
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:GoldInvoice"] =
                "Server=localhost;Database=GoldInvoiceTests;Integrated Security=True;Encrypt=True;TrustServerCertificate=True",
            ["Database:CommandTimeoutSeconds"] = "20"
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddApplication();
        services.AddInfrastructure(configuration);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<GoldInvoiceDbContext>();
        var databaseOptions = scope.ServiceProvider
            .GetRequiredService<IOptions<DatabaseOptions>>()
            .Value;
        var healthCheckOptions = provider
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value;

        Assert.Equal("Microsoft.EntityFrameworkCore.SqlServer", context.Database.ProviderName);
        Assert.Equal(20, databaseOptions.CommandTimeoutSeconds);
        Assert.Contains(
            healthCheckOptions.Registrations,
            registration => registration.Name == "database" && registration.Tags.Contains("ready"));
    }

    [Fact]
    public void AddInfrastructure_WithoutConnectionString_FailsFast()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddInfrastructure(configuration));

        Assert.DoesNotContain("Server=", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static GoldInvoiceDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<GoldInvoiceDbContext>()
            .UseSqlServer(
                "Server=localhost;Database=GoldInvoiceModelTests;Integrated Security=True;Encrypt=True;TrustServerCertificate=True")
            .Options;

        return new GoldInvoiceDbContext(options);
    }
}
