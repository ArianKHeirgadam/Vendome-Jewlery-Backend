using GoldInvoice.Domain.Catalog;
using GoldInvoice.Domain.Customers;
using GoldInvoice.Domain.Inventory;
using GoldInvoice.Domain.Orders;
using GoldInvoice.Domain.Pricing;
using GoldInvoice.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GoldInvoice.Infrastructure.Persistence.Configurations;

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders", DatabaseSchemas.Sales, table =>
        {
            table.HasCheckConstraint(
                "CK_Orders_Status",
                "[Status] IN ('Pending', 'AwaitingPayment', 'PaymentReview', 'Paid', 'Processing', 'Completed', 'Cancelled', 'Refunded')");
            table.HasCheckConstraint(
                "CK_Orders_Amounts",
                "[ItemsSubtotalRials] >= 0 AND [DiscountRials] >= 0 AND [DiscountRials] <= [ItemsSubtotalRials] AND [ShippingRials] >= 0 AND [GrandTotalRials] = [ItemsSubtotalRials] - [DiscountRials] + [ShippingRials]");
        });
        builder.ConfigureAuditable();
        builder.Property(order => order.OrderNumber).HasMaxLength(50).IsUnicode(false).IsRequired();
        builder.Property(order => order.Status).ConfigureEnum();
        builder.Property(order => order.CustomerNameSnapshot).HasMaxLength(200);
        builder.Property(order => order.CustomerNationalIdSnapshot).HasMaxLength(32).IsUnicode(false);
        builder.Property(order => order.PaidAt).HasPrecision(7);
        builder.Property(order => order.CancelledAt).HasPrecision(7);
        builder.HasIndex(order => order.OrderNumber).IsUnique();
        builder.HasIndex(order => new { order.CustomerId, order.CreatedAt });
        builder.HasIndex(order => new { order.Status, order.CreatedAt });
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(order => order.CustomerId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("OrderItems", DatabaseSchemas.Sales, table =>
        {
            table.HasCheckConstraint("CK_OrderItems_LineNumber", "[LineNumber] > 0");
            table.HasCheckConstraint("CK_OrderItems_Weight", "[WeightGrams] > 0");
            table.HasCheckConstraint("CK_OrderItems_Purity", "[Purity] BETWEEN 1 AND 1000");
            table.HasCheckConstraint(
                "CK_OrderItems_Amounts",
                "[UnitPriceRials] >= 0 AND [Quantity] > 0 AND [LineTotalRials] = [UnitPriceRials] * [Quantity]");
            table.HasCheckConstraint(
                "CK_OrderItems_AcquisitionCost",
                "([AcquisitionUnitCostRials] IS NULL AND [AcquisitionTotalCostRials] IS NULL AND [GrossProfitRials] IS NULL) OR ([AcquisitionUnitCostRials] >= 0 AND [AcquisitionTotalCostRials] = [AcquisitionUnitCostRials] * [Quantity] AND [GrossProfitRials] = [LineTotalRials] - [AcquisitionTotalCostRials])");
            table.HasCheckConstraint(
                "CK_OrderItems_PriceSnapshot",
                "([PriceCalculationSnapshotId] IS NULL AND [InventoryItemId] IS NULL AND [InventoryUnitId] IS NULL AND [NetGoldWeightGrams] IS NULL AND [Karat] IS NULL AND [MarketUnitPriceRials] IS NULL AND [GoldValueRials] IS NULL AND [WageRials] IS NULL AND [ProfitRials] IS NULL AND [TaxRials] IS NULL AND [RoundingPolicy] IS NULL) OR ([PriceCalculationSnapshotId] IS NOT NULL AND [InventoryItemId] IS NOT NULL AND [NetGoldWeightGrams] > 0 AND [NetGoldWeightGrams] <= [WeightGrams] AND [Karat] IN (9, 10, 14, 18, 21, 22, 24) AND [MarketUnitPriceRials] >= 0 AND [GoldValueRials] >= 0 AND [WageRials] >= 0 AND [ProfitRials] >= 0 AND [TaxRials] >= 0 AND [UnitPriceRials] = [GoldValueRials] + [WageRials] + [ProfitRials] + [TaxRials] AND [RoundingPolicy] IS NOT NULL)");
        });
        builder.ConfigureAuditable();
        builder.Property(item => item.Sku).HasMaxLength(64).IsUnicode(false).IsRequired();
        builder.Property(item => item.ProductName).HasMaxLength(200).IsRequired();
        builder.Property(item => item.VariantName).HasMaxLength(200).IsRequired();
        builder.Property(item => item.WeightGrams).HasPrecision(18, 3);
        builder.Property(item => item.NetGoldWeightGrams).HasPrecision(18, 3);
        builder.Property(item => item.MarketUnitPriceRials).HasColumnType("bigint");
        builder.Property(item => item.GoldValueRials).HasColumnType("bigint");
        builder.Property(item => item.WageRials).HasColumnType("bigint");
        builder.Property(item => item.ProfitRials).HasColumnType("bigint");
        builder.Property(item => item.TaxRials).HasColumnType("bigint");
        builder.Property(item => item.AcquisitionUnitCostRials).HasColumnType("bigint");
        builder.Property(item => item.AcquisitionTotalCostRials).HasColumnType("bigint");
        builder.Property(item => item.GrossProfitRials).HasColumnType("bigint");
        builder.Property(item => item.RoundingPolicy).HasMaxLength(100).IsUnicode(false);
        builder.HasIndex(item => new { item.OrderId, item.LineNumber }).IsUnique();
        builder.HasIndex(item => item.PriceCalculationSnapshotId)
            .IsUnique()
            .HasFilter("[PriceCalculationSnapshotId] IS NOT NULL");
        builder.HasIndex(item => item.InventoryUnitId);
        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(item => item.OrderId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ProductVariant>()
            .WithMany()
            .HasForeignKey(item => item.ProductVariantId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<PriceCalculationSnapshot>()
            .WithMany()
            .HasForeignKey(item => item.PriceCalculationSnapshotId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<InventoryItem>()
            .WithMany()
            .HasForeignKey(item => item.InventoryItemId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<InventoryUnit>()
            .WithMany()
            .HasForeignKey(item => item.InventoryUnitId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class OrderStatusHistoryConfiguration : IEntityTypeConfiguration<OrderStatusHistory>
{
    public void Configure(EntityTypeBuilder<OrderStatusHistory> builder)
    {
        builder.ToTable("OrderStatusHistory", DatabaseSchemas.Sales, table =>
        {
            table.HasCheckConstraint(
                "CK_OrderStatusHistory_ToStatus",
                "[ToStatus] IN ('Pending', 'AwaitingPayment', 'PaymentReview', 'Paid', 'Processing', 'Completed', 'Cancelled', 'Refunded')");
            table.HasCheckConstraint(
                "CK_OrderStatusHistory_FromStatus",
                "[FromStatus] IS NULL OR [FromStatus] IN ('Pending', 'AwaitingPayment', 'PaymentReview', 'Paid', 'Processing', 'Completed', 'Cancelled', 'Refunded')");
        });
        builder.ConfigureAuditable();
        builder.Property(history => history.FromStatus).ConfigureNullableEnum();
        builder.Property(history => history.ToStatus).ConfigureEnum();
        builder.Property(history => history.Reason).HasMaxLength(1000);
        builder.Property(history => history.OccurredAt).HasPrecision(7);
        builder.HasIndex(history => new { history.OrderId, history.OccurredAt });
        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(history => history.OrderId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(history => history.ChangedBy)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class OrderStoreSnapshotConfiguration : IEntityTypeConfiguration<OrderStoreSnapshot>
{
    public void Configure(EntityTypeBuilder<OrderStoreSnapshot> builder)
    {
        builder.ToTable("OrderStoreSnapshots", DatabaseSchemas.Sales);
        builder.ConfigureAuditable();
        builder.Property(snapshot => snapshot.TradeName).HasMaxLength(200).IsRequired();
        builder.Property(snapshot => snapshot.LegalName).HasMaxLength(200).IsRequired();
        builder.Property(snapshot => snapshot.NationalId).HasMaxLength(32).IsUnicode(false);
        builder.Property(snapshot => snapshot.EconomicCode).HasMaxLength(32).IsUnicode(false);
        builder.Property(snapshot => snapshot.RegistrationNumber).HasMaxLength(32).IsUnicode(false);
        builder.Property(snapshot => snapshot.PhoneNumber).HasMaxLength(32).IsUnicode(false).IsRequired();
        builder.Property(snapshot => snapshot.PostalCode).HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(snapshot => snapshot.AddressLine).HasMaxLength(1000).IsRequired();
        builder.HasIndex(snapshot => snapshot.OrderId).IsUnique();
        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(snapshot => snapshot.OrderId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class OrderAddressSnapshotConfiguration : IEntityTypeConfiguration<OrderAddressSnapshot>
{
    public void Configure(EntityTypeBuilder<OrderAddressSnapshot> builder)
    {
        builder.ToTable("OrderAddressSnapshots", DatabaseSchemas.Sales);
        builder.ConfigureAuditable();
        builder.Property(address => address.RecipientName).HasMaxLength(200).IsRequired();
        builder.Property(address => address.PhoneNumber).HasMaxLength(32).IsUnicode(false).IsRequired();
        builder.Property(address => address.Province).HasMaxLength(100).IsRequired();
        builder.Property(address => address.City).HasMaxLength(100).IsRequired();
        builder.Property(address => address.PostalCode).HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(address => address.AddressLine).HasMaxLength(1000).IsRequired();
        builder.HasIndex(address => address.OrderId).IsUnique();
        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(address => address.OrderId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<CustomerAddress>()
            .WithMany()
            .HasForeignKey(address => address.CustomerAddressId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
