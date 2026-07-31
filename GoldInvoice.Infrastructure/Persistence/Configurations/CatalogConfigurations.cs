using GoldInvoice.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GoldInvoice.Infrastructure.Persistence.Configurations;

internal sealed class ProductCategoryConfiguration : IEntityTypeConfiguration<ProductCategory>
{
    public void Configure(EntityTypeBuilder<ProductCategory> builder)
    {
        builder.ToTable("ProductCategories", DatabaseSchemas.Catalog, table =>
        {
            table.HasCheckConstraint("CK_ProductCategories_DisplayOrder", "[DisplayOrder] >= 0");
            table.HasCheckConstraint(
                "CK_ProductCategories_Parent",
                "[ParentCategoryId] IS NULL OR [ParentCategoryId] <> [Id]");
        });
        builder.ConfigureAuditable();
        builder.Property(category => category.Name).HasMaxLength(200).IsRequired();
        builder.Property(category => category.Slug).HasMaxLength(200).IsUnicode(false).IsRequired();
        builder.Property(category => category.IsActive).HasDefaultValue(true);
        builder.HasIndex(category => category.Slug).IsUnique();
        builder.HasIndex(category => new { category.ParentCategoryId, category.DisplayOrder, category.Name });
        builder.HasOne<ProductCategory>()
            .WithMany()
            .HasForeignKey(category => category.ParentCategoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products", DatabaseSchemas.Catalog, table =>
        {
            table.HasCheckConstraint(
                "CK_Products_SoftDelete",
                "([IsDeleted] = 0 AND [DeletedAt] IS NULL) OR ([IsDeleted] = 1 AND [DeletedAt] IS NOT NULL)");
        });
        builder.ConfigureSoftDelete();
        builder.Property(product => product.Name).HasMaxLength(200).IsRequired();
        builder.Property(product => product.Slug).HasMaxLength(200).IsUnicode(false).IsRequired();
        builder.Property(product => product.Description).HasMaxLength(4000);
        builder.Property(product => product.IsActive).HasDefaultValue(true);
        builder.HasIndex(product => product.Slug)
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");
        builder.HasIndex(product => new { product.IsActive, product.Name })
            .HasFilter("[IsDeleted] = 0");
        builder.HasIndex(product => new { product.ProductCategoryId, product.IsActive })
            .HasFilter("[IsDeleted] = 0");
        builder.HasOne<ProductCategory>()
            .WithMany()
            .HasForeignKey(product => product.ProductCategoryId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class GoldProductDetailConfiguration : IEntityTypeConfiguration<GoldProductDetail>
{
    public void Configure(EntityTypeBuilder<GoldProductDetail> builder)
    {
        builder.ToTable("GoldProductDetails", DatabaseSchemas.Catalog, table =>
        {
            table.HasCheckConstraint("CK_GoldProductDetails_Karat", "[Karat] IN (9, 10, 14, 18, 21, 22, 24)");
            table.HasCheckConstraint("CK_GoldProductDetails_GrossWeight", "[GrossWeight] > 0");
            table.HasCheckConstraint("CK_GoldProductDetails_NetGoldWeight", "[NetGoldWeight] > 0");
            table.HasCheckConstraint(
                "CK_GoldProductDetails_ComponentWeights",
                "[StoneWeight] >= 0 AND [OtherMaterialWeight] >= 0 AND ([NetGoldWeight] + [StoneWeight] + [OtherMaterialWeight]) <= [GrossWeight]");
            table.HasCheckConstraint(
                "CK_GoldProductDetails_StoneState",
                "([HasStone] = 1 AND [StoneWeight] > 0) OR ([HasStone] = 0 AND [StoneWeight] = 0)");
            table.HasCheckConstraint(
                "CK_GoldProductDetails_Wage",
                "([ManufacturingWageType] IN ('FixedRials', 'PerGramRials') AND [ManufacturingWageAmountRials] IS NOT NULL AND [ManufacturingWageAmountRials] >= 0 AND [ManufacturingWagePercentage] IS NULL) OR " +
                "([ManufacturingWageType] = 'PercentageOfGoldValue' AND [ManufacturingWageAmountRials] IS NULL AND [ManufacturingWagePercentage] BETWEEN 0 AND 100)");
            table.HasCheckConstraint(
                "CK_GoldProductDetails_Percentages",
                "[ProfitPercentage] BETWEEN 0 AND 100 AND [TaxPercentage] BETWEEN 0 AND 100");
        });
        builder.ConfigureAuditable();
        builder.Property(detail => detail.GrossWeight).HasPrecision(18, 3);
        builder.Property(detail => detail.NetGoldWeight).HasPrecision(18, 3);
        builder.Property(detail => detail.StoneWeight).HasPrecision(18, 3);
        builder.Property(detail => detail.OtherMaterialWeight).HasPrecision(18, 3);
        builder.Property(detail => detail.ManufacturingWageType).ConfigureEnum();
        builder.Ignore(detail => detail.ManufacturingWageValue);
        builder.Property(detail => detail.ManufacturingWagePercentage).HasPrecision(9, 4);
        builder.Property(detail => detail.ProfitPercentage).HasPrecision(9, 4);
        builder.Property(detail => detail.TaxPercentage).HasPrecision(9, 4);
        builder.HasIndex(detail => detail.ProductVariantId).IsUnique();
        builder.HasOne<ProductVariant>()
            .WithOne()
            .HasForeignKey<GoldProductDetail>(detail => detail.ProductVariantId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class ProductVariantConfiguration : IEntityTypeConfiguration<ProductVariant>
{
    public void Configure(EntityTypeBuilder<ProductVariant> builder)
    {
        builder.ToTable("ProductVariants", DatabaseSchemas.Catalog, table =>
        {
            table.HasCheckConstraint("CK_ProductVariants_Weight", "[WeightGrams] > 0");
            table.HasCheckConstraint("CK_ProductVariants_Purity", "[Purity] BETWEEN 1 AND 1000");
            table.HasCheckConstraint("CK_ProductVariants_LaborFee", "[LaborFeeRials] >= 0");
            table.HasCheckConstraint(
                "CK_ProductVariants_FixedPrice",
                "[FixedPriceRials] IS NULL OR [FixedPriceRials] >= 0");
            table.HasCheckConstraint(
                "CK_ProductVariants_SoftDelete",
                "([IsDeleted] = 0 AND [DeletedAt] IS NULL) OR ([IsDeleted] = 1 AND [DeletedAt] IS NOT NULL)");
        });
        builder.ConfigureSoftDelete();
        builder.Property(variant => variant.Sku).HasMaxLength(64).IsUnicode(false).IsRequired();
        builder.Property(variant => variant.Name).HasMaxLength(200).IsRequired();
        builder.Property(variant => variant.WeightGrams).HasPrecision(18, 3);
        builder.Property(variant => variant.IsActive).HasDefaultValue(true);
        builder.HasIndex(variant => variant.Sku)
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");
        builder.HasIndex(variant => new { variant.ProductId, variant.IsActive })
            .HasFilter("[IsDeleted] = 0");
        builder.HasAlternateKey(variant => new { variant.ProductId, variant.Id });
        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(variant => variant.ProductId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class ProductImageConfiguration : IEntityTypeConfiguration<ProductImage>
{
    public void Configure(EntityTypeBuilder<ProductImage> builder)
    {
        builder.ToTable("ProductImages", DatabaseSchemas.Catalog, table =>
        {
            table.HasCheckConstraint("CK_ProductImages_SortOrder", "[SortOrder] >= 0");
            table.HasCheckConstraint(
                "CK_ProductImages_SoftDelete",
                "([IsDeleted] = 0 AND [DeletedAt] IS NULL) OR ([IsDeleted] = 1 AND [DeletedAt] IS NOT NULL)");
        });
        builder.ConfigureSoftDelete();
        builder.Property(image => image.StorageKey).HasMaxLength(500).IsUnicode(false).IsRequired();
        builder.Property(image => image.ContentType).HasMaxLength(100).IsUnicode(false).IsRequired();
        builder.Property(image => image.AltText).HasMaxLength(300);
        builder.Property(image => image.SortOrder).HasDefaultValue(0);
        builder.Property(image => image.IsPrimary).HasDefaultValue(false);
        builder.HasIndex(image => image.StorageKey).IsUnique();
        builder.HasIndex(image => new { image.ProductId, image.SortOrder })
            .HasFilter("[IsDeleted] = 0");
        builder.HasIndex(image => new { image.ProductId, image.ProductVariantId })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0 AND [IsPrimary] = 1");
        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(image => image.ProductId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ProductVariant>()
            .WithMany()
            .HasForeignKey(
                nameof(ProductImage.ProductId),
                nameof(ProductImage.ProductVariantId))
            .HasPrincipalKey(
                nameof(ProductVariant.ProductId),
                nameof(ProductVariant.Id))
            .OnDelete(DeleteBehavior.NoAction);
    }
}
