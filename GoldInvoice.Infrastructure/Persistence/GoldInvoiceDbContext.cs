using GoldInvoice.Domain.Catalog;
using GoldInvoice.Domain.Business;
using GoldInvoice.Domain.Customers;
using GoldInvoice.Domain.Inventory;
using GoldInvoice.Domain.Invoicing;
using GoldInvoice.Domain.Orders;
using GoldInvoice.Domain.Payments;
using GoldInvoice.Domain.Platform;
using GoldInvoice.Domain.Pricing;
using GoldInvoice.Domain.Security;
using GoldInvoice.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace GoldInvoice.Infrastructure.Persistence;

public sealed class GoldInvoiceDbContext(
    DbContextOptions<GoldInvoiceDbContext> options)
    : IdentityDbContext<
        ApplicationUser,
        ApplicationRole,
        Guid,
        IdentityUserClaim<Guid>,
        IdentityUserRole<Guid>,
        IdentityUserLogin<Guid>,
        IdentityRoleClaim<Guid>,
        IdentityUserToken<Guid>>(options)
{
    public DbSet<Permission> Permissions => Set<Permission>();

    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<UserSession> UserSessions => Set<UserSession>();

    public DbSet<TrustedDevice> TrustedDevices => Set<TrustedDevice>();

    public DbSet<LoginAttempt> LoginAttempts => Set<LoginAttempt>();

    public DbSet<SecurityEvent> SecurityEvents => Set<SecurityEvent>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();

    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();

    public DbSet<ProductImage> ProductImages => Set<ProductImage>();

    public DbSet<GoldProductDetail> GoldProductDetails => Set<GoldProductDetail>();

    public DbSet<ProductPricingRule> ProductPricingRules => Set<ProductPricingRule>();

    public DbSet<MarketPriceSource> MarketPriceSources => Set<MarketPriceSource>();

    public DbSet<MarketPriceSnapshot> MarketPriceSnapshots => Set<MarketPriceSnapshot>();

    public DbSet<PriceCalculationSnapshot> PriceCalculationSnapshots => Set<PriceCalculationSnapshot>();

    public DbSet<Warehouse> Warehouses => Set<Warehouse>();

    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();

    public DbSet<StockMovement> StockMovements => Set<StockMovement>();

    public DbSet<StockReservation> StockReservations => Set<StockReservation>();

    public DbSet<InventoryAdjustment> InventoryAdjustments => Set<InventoryAdjustment>();

    public DbSet<InventoryUnit> InventoryUnits => Set<InventoryUnit>();

    public DbSet<CustomerAddress> CustomerAddresses => Set<CustomerAddress>();

    public DbSet<Supplier> Suppliers => Set<Supplier>();

    public DbSet<SupplierPurchase> SupplierPurchases => Set<SupplierPurchase>();

    public DbSet<CustomerInteraction> CustomerInteractions => Set<CustomerInteraction>();

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    public DbSet<OrderStatusHistory> OrderStatusHistory => Set<OrderStatusHistory>();

    public DbSet<OrderAddressSnapshot> OrderAddressSnapshots => Set<OrderAddressSnapshot>();

    public DbSet<OrderStoreSnapshot> OrderStoreSnapshots => Set<OrderStoreSnapshot>();

    public DbSet<PaymentGateway> PaymentGateways => Set<PaymentGateway>();

    public DbSet<Payment> Payments => Set<Payment>();

    public DbSet<PaymentAttempt> PaymentAttempts => Set<PaymentAttempt>();

    public DbSet<PaymentCallback> PaymentCallbacks => Set<PaymentCallback>();

    public DbSet<Invoice> Invoices => Set<Invoice>();

    public DbSet<InvoiceSequence> InvoiceSequences => Set<InvoiceSequence>();

    public DbSet<InvoiceItem> InvoiceItems => Set<InvoiceItem>();

    public DbSet<InvoiceAddressSnapshot> InvoiceAddressSnapshots => Set<InvoiceAddressSnapshot>();

    public DbSet<InvoiceStoreSnapshot> InvoiceStoreSnapshots => Set<InvoiceStoreSnapshot>();

    public DbSet<InvoicePrintLog> InvoicePrintLogs => Set<InvoicePrintLog>();

    public DbSet<DesktopDevice> DesktopDevices => Set<DesktopDevice>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(GoldInvoiceDbContext).Assembly);
    }
}
