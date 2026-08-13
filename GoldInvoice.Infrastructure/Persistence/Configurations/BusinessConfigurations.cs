using GoldInvoice.Domain.Business;
using GoldInvoice.Domain.Catalog;
using GoldInvoice.Domain.Inventory;
using GoldInvoice.Domain.Pricing;
using GoldInvoice.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GoldInvoice.Infrastructure.Persistence.Configurations;

internal sealed class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.ToTable("Suppliers", DatabaseSchemas.Business, table =>
        {
            table.HasCheckConstraint(
                "CK_Suppliers_SoftDelete",
                "([IsDeleted] = 0 AND [DeletedAt] IS NULL) OR ([IsDeleted] = 1 AND [DeletedAt] IS NOT NULL)");
        });
        builder.ConfigureSoftDelete();
        builder.Property(supplier => supplier.Code).HasMaxLength(64).IsUnicode(false).IsRequired();
        builder.Property(supplier => supplier.Name).HasMaxLength(200).IsRequired();
        builder.Property(supplier => supplier.ContactName).HasMaxLength(200);
        builder.Property(supplier => supplier.PhoneNumber).HasMaxLength(32).IsUnicode(false);
        builder.Property(supplier => supplier.Email).HasMaxLength(256).IsUnicode(false);
        builder.Property(supplier => supplier.NationalId).HasMaxLength(32).IsUnicode(false);
        builder.Property(supplier => supplier.AddressLine).HasMaxLength(1000);
        builder.Property(supplier => supplier.Notes).HasMaxLength(2000);
        builder.Property(supplier => supplier.IsActive).HasDefaultValue(true);
        builder.HasIndex(supplier => supplier.Code)
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");
        builder.HasIndex(supplier => new { supplier.IsActive, supplier.Name });
    }
}

internal sealed class CustomerInteractionConfiguration : IEntityTypeConfiguration<CustomerInteraction>
{
    public void Configure(EntityTypeBuilder<CustomerInteraction> builder)
    {
        builder.ToTable("CustomerInteractions", DatabaseSchemas.Crm, table =>
        {
            table.HasCheckConstraint(
                "CK_CustomerInteractions_FollowUp",
                "[NextFollowUpAt] IS NULL OR [NextFollowUpAt] > [OccurredAt]");
            table.HasCheckConstraint(
                "CK_CustomerInteractions_Completion",
                "([Status] = 'Completed' AND [CompletedAt] IS NOT NULL) OR ([Status] <> 'Completed' AND [CompletedAt] IS NULL)");
        });
        builder.ConfigureAuditable();
        builder.Property(interaction => interaction.InteractionType).ConfigureEnum();
        builder.Property(interaction => interaction.Subject).HasMaxLength(200).IsRequired();
        builder.Property(interaction => interaction.Notes).HasMaxLength(4000);
        builder.Property(interaction => interaction.OccurredAt).HasPrecision(7);
        builder.Property(interaction => interaction.NextFollowUpAt).HasPrecision(7);
        builder.Property(interaction => interaction.Status).ConfigureEnum();
        builder.Property(interaction => interaction.CompletedAt).HasPrecision(7);
        builder.HasIndex(interaction => new { interaction.CustomerId, interaction.OccurredAt });
        builder.HasIndex(interaction => new { interaction.Status, interaction.NextFollowUpAt });
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(interaction => interaction.CustomerId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class SupplierPurchaseConfiguration : IEntityTypeConfiguration<SupplierPurchase>
{
    public void Configure(EntityTypeBuilder<SupplierPurchase> builder)
    {
        builder.ToTable("SupplierPurchases", DatabaseSchemas.Business, table =>
        {
            table.HasCheckConstraint("CK_SupplierPurchases_Quantity", "[Quantity] > 0");
            table.HasCheckConstraint(
                "CK_SupplierPurchases_Amounts",
                "[UnitCostRials] >= 0 AND [SellingUnitPriceRials] > 0 AND [TotalCostRials] = [UnitCostRials] * [Quantity]");
        });
        builder.ConfigureAuditable();
        builder.Ignore(purchase => purchase.ExpectedUnitProfitRials);
        builder.Ignore(purchase => purchase.ExpectedTotalProfitRials);
        builder.Property(purchase => purchase.PurchaseNumber).HasMaxLength(50).IsUnicode(false).IsRequired();
        builder.Property(purchase => purchase.UnitCostRials).HasColumnType("bigint");
        builder.Property(purchase => purchase.TotalCostRials).HasColumnType("bigint");
        builder.Property(purchase => purchase.SellingUnitPriceRials).HasColumnType("bigint");
        builder.Property(purchase => purchase.PurchasedAt).HasPrecision(7);
        builder.Property(purchase => purchase.SupplierReference).HasMaxLength(100);
        builder.Property(purchase => purchase.Notes).HasMaxLength(1000);
        builder.HasIndex(purchase => purchase.PurchaseNumber).IsUnique();
        builder.HasIndex(purchase => new { purchase.SupplierId, purchase.PurchasedAt });
        builder.HasIndex(purchase => new { purchase.ProductVariantId, purchase.PurchasedAt });
        builder.HasIndex(purchase => purchase.StockMovementId).IsUnique();
        builder.HasOne<Supplier>().WithMany().HasForeignKey(purchase => purchase.SupplierId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Warehouse>().WithMany().HasForeignKey(purchase => purchase.WarehouseId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ProductVariant>().WithMany().HasForeignKey(purchase => purchase.ProductVariantId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<InventoryItem>().WithMany().HasForeignKey(purchase => purchase.InventoryItemId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<StockMovement>().WithMany().HasForeignKey(purchase => purchase.StockMovementId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ProductPricingRule>().WithMany().HasForeignKey(purchase => purchase.PricingRuleId).OnDelete(DeleteBehavior.NoAction);
    }
}
