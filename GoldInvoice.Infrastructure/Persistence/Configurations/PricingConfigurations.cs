using GoldInvoice.Domain.Catalog;
using GoldInvoice.Domain.Pricing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GoldInvoice.Infrastructure.Persistence.Configurations;

internal sealed class ProductPricingRuleConfiguration : IEntityTypeConfiguration<ProductPricingRule>
{
    public void Configure(EntityTypeBuilder<ProductPricingRule> builder)
    {
        builder.ToTable("ProductPricingRules", DatabaseSchemas.Pricing, table =>
        {
            table.HasCheckConstraint(
                "CK_ProductPricingRules_Window",
                "[EffectiveTo] IS NULL OR [EffectiveTo] > [EffectiveFrom]");
            table.HasCheckConstraint(
                "CK_ProductPricingRules_Amounts",
                "([FixedPriceRials] IS NULL OR [FixedPriceRials] >= 0) AND ([FixedGoldPricePerGramRials] IS NULL OR [FixedGoldPricePerGramRials] >= 0)");
            table.HasCheckConstraint(
                "CK_ProductPricingRules_Wage",
                "([WageType] IN ('FixedRials', 'PerGramRials') AND [WageAmountRials] IS NOT NULL AND [WageAmountRials] >= 0 AND [WagePercentage] IS NULL) OR " +
                "([WageType] = 'PercentageOfGoldValue' AND [WageAmountRials] IS NULL AND [WagePercentage] BETWEEN 0 AND 100)");
            table.HasCheckConstraint(
                "CK_ProductPricingRules_Percentages",
                "[ProfitPercentage] BETWEEN 0 AND 100 AND [TaxPercentage] BETWEEN 0 AND 100");
            table.HasCheckConstraint(
                "CK_ProductPricingRules_MethodInputs",
                "([PricingMethod] = 'FixedPrice' AND [FixedPriceRials] > 0) OR " +
                "([PricingMethod] = 'WeightBased' AND [FixedGoldPricePerGramRials] > 0) OR " +
                "([PricingMethod] = 'MarketBased' AND [GoldMarketPriceType] IN ('Gold18K', 'Gold24K')) OR " +
                "([PricingMethod] = 'ManualReview')");
        });
        builder.ConfigureAuditable();
        builder.Property(rule => rule.PricingMethod).ConfigureEnum();
        builder.Property(rule => rule.GoldMarketPriceType).ConfigureNullableEnum();
        builder.Property(rule => rule.WageType).ConfigureEnum();
        builder.Ignore(rule => rule.WageValue);
        builder.Property(rule => rule.WagePercentage).HasPrecision(9, 4);
        builder.Property(rule => rule.ProfitPercentage).HasPrecision(9, 4);
        builder.Property(rule => rule.TaxPercentage).HasPrecision(9, 4);
        builder.Property(rule => rule.EffectiveFrom).HasPrecision(7);
        builder.Property(rule => rule.EffectiveTo).HasPrecision(7);
        builder.Property(rule => rule.IsActive).HasDefaultValue(true);
        builder.HasIndex(rule => new { rule.ProductVariantId, rule.IsActive, rule.EffectiveFrom, rule.EffectiveTo });
        builder.HasOne<ProductVariant>()
            .WithMany()
            .HasForeignKey(rule => rule.ProductVariantId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class MarketPriceSourceConfiguration : IEntityTypeConfiguration<MarketPriceSource>
{
    public void Configure(EntityTypeBuilder<MarketPriceSource> builder)
    {
        builder.ToTable("MarketPriceSources", DatabaseSchemas.Pricing, table =>
            table.HasCheckConstraint("CK_MarketPriceSources_Priority", "[Priority] >= 0"));
        builder.ConfigureAuditable();
        builder.Property(source => source.Name).HasMaxLength(200).IsRequired();
        builder.Property(source => source.ProviderCode).HasMaxLength(100).IsUnicode(false).IsRequired();
        builder.Property(source => source.BaseUrl).HasMaxLength(500).IsUnicode(false);
        builder.Property(source => source.ConfigurationReference).HasMaxLength(500).IsUnicode(false);
        builder.Property(source => source.LastSuccessfulFetchAt).HasPrecision(7);
        builder.Property(source => source.LastFailureAt).HasPrecision(7);
        builder.Property(source => source.IsActive).HasDefaultValue(true);
        builder.HasIndex(source => source.ProviderCode).IsUnique();
        builder.HasIndex(source => new { source.IsActive, source.Priority });
    }
}

internal sealed class MarketPriceSnapshotConfiguration : IEntityTypeConfiguration<MarketPriceSnapshot>
{
    public void Configure(EntityTypeBuilder<MarketPriceSnapshot> builder)
    {
        builder.ToTable("MarketPriceSnapshots", DatabaseSchemas.Pricing, table =>
        {
            table.HasCheckConstraint(
                "CK_MarketPriceSnapshots_Prices",
                "[BuyPriceRials] >= 0 AND [SellPriceRials] >= 0");
            table.HasCheckConstraint(
                "CK_MarketPriceSnapshots_ValidState",
                "([IsValid] = 1 AND [ValidationStatus] = 'Accepted' AND [BuyPriceRials] > 0 AND [SellPriceRials] >= [BuyPriceRials]) OR ([IsValid] = 0 AND [ValidationStatus] <> 'Accepted')");
        });
        builder.ConfigureAuditable();
        builder.Property(snapshot => snapshot.PriceType).ConfigureEnum();
        builder.Property(snapshot => snapshot.ValidationStatus).ConfigureEnum();
        builder.Property(snapshot => snapshot.CapturedAt).HasPrecision(7);
        builder.Property(snapshot => snapshot.ProviderTimestamp).HasPrecision(7);
        builder.Property(snapshot => snapshot.RawPayloadHash).HasMaxLength(128).IsUnicode(false).IsRequired();
        builder.HasIndex(snapshot => new { snapshot.SourceId, snapshot.PriceType, snapshot.CapturedAt }).IsUnique();
        builder.HasIndex(snapshot => new { snapshot.PriceType, snapshot.IsValid, snapshot.CapturedAt });
        builder.HasIndex(snapshot => new
        {
            snapshot.SourceId,
            snapshot.PriceType,
            snapshot.RawPayloadHash
        }).IsUnique();
        builder.HasOne<MarketPriceSource>()
            .WithMany()
            .HasForeignKey(snapshot => snapshot.SourceId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class PriceCalculationSnapshotConfiguration : IEntityTypeConfiguration<PriceCalculationSnapshot>
{
    public void Configure(EntityTypeBuilder<PriceCalculationSnapshot> builder)
    {
        builder.ToTable("PriceCalculationSnapshots", DatabaseSchemas.Pricing, table =>
        {
            table.HasCheckConstraint(
                "CK_PriceCalculationSnapshots_Weights",
                "[GrossWeight] > 0 AND [NetGoldWeight] > 0 AND [NetGoldWeight] <= [GrossWeight]");
            table.HasCheckConstraint("CK_PriceCalculationSnapshots_Karat", "[Karat] IN (9, 10, 14, 18, 21, 22, 24)");
            table.HasCheckConstraint(
                "CK_PriceCalculationSnapshots_Amounts",
                "[MarketUnitPriceRials] >= 0 AND [GoldValueRials] >= 0 AND [WageRials] >= 0 AND [ProfitRials] >= 0 AND [TaxRials] >= 0 AND [FinalPriceRials] = [GoldValueRials] + [WageRials] + [ProfitRials] + [TaxRials]");
        });
        builder.ConfigureAuditable();
        builder.Property(snapshot => snapshot.PricingMethod).ConfigureEnum();
        builder.Property(snapshot => snapshot.GrossWeight).HasPrecision(18, 3);
        builder.Property(snapshot => snapshot.NetGoldWeight).HasPrecision(18, 3);
        builder.Property(snapshot => snapshot.CalculatedAt).HasPrecision(7);
        builder.Property(snapshot => snapshot.RoundingPolicy).HasMaxLength(100).IsUnicode(false).IsRequired();
        builder.HasIndex(snapshot => new { snapshot.ProductVariantId, snapshot.CalculatedAt });
        builder.HasIndex(snapshot => snapshot.PricingRuleId);
        builder.HasIndex(snapshot => snapshot.MarketPriceSnapshotId)
            .HasFilter("[MarketPriceSnapshotId] IS NOT NULL");
        builder.HasOne<ProductVariant>()
            .WithMany()
            .HasForeignKey(snapshot => snapshot.ProductVariantId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ProductPricingRule>()
            .WithMany()
            .HasForeignKey(snapshot => snapshot.PricingRuleId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<MarketPriceSnapshot>()
            .WithMany()
            .HasForeignKey(snapshot => snapshot.MarketPriceSnapshotId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
