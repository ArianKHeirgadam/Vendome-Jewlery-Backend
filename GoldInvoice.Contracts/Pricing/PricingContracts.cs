using System.ComponentModel.DataAnnotations;

namespace GoldInvoice.Contracts.Pricing;

public sealed class ProductPricingRuleResponse
{
    public required Guid Id { get; init; }
    public required Guid ProductVariantId { get; init; }
    public required string PricingMethod { get; init; }
    public string? GoldMarketPriceType { get; init; }
    public long? FixedPriceRials { get; init; }
    public long? FixedGoldPricePerGramRials { get; init; }
    public required string WageType { get; init; }
    public required decimal WageValue { get; init; }
    public required decimal ProfitPercentage { get; init; }
    public required decimal TaxPercentage { get; init; }
    public required DateTimeOffset EffectiveFrom { get; init; }
    public DateTimeOffset? EffectiveTo { get; init; }
    public required bool IsActive { get; init; }
    public required string RowVersion { get; init; }
}

public sealed class CreateProductPricingRuleRequest
{
    public Guid ProductVariantId { get; init; }

    [Required]
    public string PricingMethod { get; init; } = string.Empty;

    public string? GoldMarketPriceType { get; init; }
    public long? FixedPriceRials { get; init; }
    public long? FixedGoldPricePerGramRials { get; init; }

    [Required]
    public string WageType { get; init; } = string.Empty;

    public decimal WageValue { get; init; }
    public decimal ProfitPercentage { get; init; }
    public decimal TaxPercentage { get; init; }
    public DateTimeOffset EffectiveFrom { get; init; }
    public DateTimeOffset? EffectiveTo { get; init; }
}

public sealed class MarketPriceSourceResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string ProviderCode { get; init; }
    public required bool IsActive { get; init; }
    public required int Priority { get; init; }
    public string? BaseUrl { get; init; }
    public string? ConfigurationReference { get; init; }
    public DateTimeOffset? LastSuccessfulFetchAt { get; init; }
    public DateTimeOffset? LastFailureAt { get; init; }
    public required string RowVersion { get; init; }
}

public sealed class CreateMarketPriceSourceRequest
{
    [Required, StringLength(200)]
    public string Name { get; init; } = string.Empty;

    [Required, StringLength(100)]
    public string ProviderCode { get; init; } = string.Empty;

    [Range(0, int.MaxValue)]
    public int Priority { get; init; }

    [StringLength(500)]
    public string? BaseUrl { get; init; }

    [StringLength(500)]
    public string? ConfigurationReference { get; init; }
}

public sealed class MarketPriceResponse
{
    public required Guid Id { get; init; }
    public required Guid SourceId { get; init; }
    public required string PriceType { get; init; }
    public required long BuyPriceRials { get; init; }
    public required long SellPriceRials { get; init; }
    public required DateTimeOffset CapturedAt { get; init; }
    public DateTimeOffset? ProviderTimestamp { get; init; }
    public required string ValidationStatus { get; init; }
}

public sealed class CalculateProductPriceRequest
{
    public Guid ProductVariantId { get; init; }
    public decimal? ActualGrossWeight { get; init; }
    public decimal? ActualNetGoldWeight { get; init; }
}

public sealed class CalculatedProductPriceResponse
{
    public required Guid SnapshotId { get; init; }
    public required Guid ProductVariantId { get; init; }
    public required Guid PricingRuleId { get; init; }
    public Guid? MarketPriceSnapshotId { get; init; }
    public required long MarketUnitPriceRials { get; init; }
    public required long GoldValueRials { get; init; }
    public required long WageRials { get; init; }
    public required long ProfitRials { get; init; }
    public required long TaxRials { get; init; }
    public required long FinalPriceRials { get; init; }
    public required DateTimeOffset CalculatedAt { get; init; }
    public required string RoundingPolicy { get; init; }
}
