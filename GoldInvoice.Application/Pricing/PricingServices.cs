using GoldInvoice.Domain.Catalog;
using GoldInvoice.Domain.Pricing;

namespace GoldInvoice.Application.Pricing;

public sealed record ProductPricingRuleInfo(
    Guid Id,
    Guid ProductVariantId,
    PricingMethod PricingMethod,
    MarketPriceType? GoldMarketPriceType,
    long? FixedPriceRials,
    long? FixedGoldPricePerGramRials,
    ManufacturingWageType WageType,
    decimal WageValue,
    decimal ProfitPercentage,
    decimal TaxPercentage,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    bool IsActive,
    string RowVersion);

public sealed record CreateProductPricingRuleCommand(
    Guid ProductVariantId,
    PricingMethod PricingMethod,
    MarketPriceType? GoldMarketPriceType,
    long? FixedPriceRials,
    long? FixedGoldPricePerGramRials,
    ManufacturingWageType WageType,
    decimal WageValue,
    decimal ProfitPercentage,
    decimal TaxPercentage,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo);

public sealed record MarketPriceSourceInfo(
    Guid Id,
    string Name,
    string ProviderCode,
    bool IsActive,
    int Priority,
    string? BaseUrl,
    string? ConfigurationReference,
    DateTimeOffset? LastSuccessfulFetchAt,
    DateTimeOffset? LastFailureAt,
    string RowVersion);

public sealed record CreateMarketPriceSourceCommand(
    string Name,
    string ProviderCode,
    int Priority,
    string? BaseUrl,
    string? ConfigurationReference);

public sealed record MarketPriceInfo(
    Guid Id,
    Guid SourceId,
    MarketPriceType PriceType,
    long BuyPriceRials,
    long SellPriceRials,
    DateTimeOffset CapturedAt,
    DateTimeOffset? ProviderTimestamp,
    string ValidationStatus);

public sealed record CalculateProductPriceCommand(
    Guid ProductVariantId,
    decimal? ActualGrossWeight,
    decimal? ActualNetGoldWeight);

public sealed record CalculatedProductPriceInfo(
    Guid SnapshotId,
    Guid ProductVariantId,
    Guid PricingRuleId,
    Guid? MarketPriceSnapshotId,
    long MarketUnitPriceRials,
    long GoldValueRials,
    long WageRials,
    long ProfitRials,
    long TaxRials,
    long FinalPriceRials,
    DateTimeOffset CalculatedAt,
    string RoundingPolicy);

public sealed record MarketPriceQuote(
    MarketPriceType PriceType,
    long BuyPriceRials,
    long SellPriceRials,
    DateTimeOffset? ProviderTimestamp,
    string RawPayloadHash);

public interface IMarketPriceProvider
{
    string ProviderCode { get; }

    Task<IReadOnlyList<MarketPriceQuote>> FetchAsync(CancellationToken cancellationToken);
}

public interface IMarketPriceIngestionService
{
    Task<int> PollAllAsync(CancellationToken cancellationToken);

    Task<int> PollSourceAsync(string providerCode, CancellationToken cancellationToken);
}

public interface IProductPricingService
{
    Task<IReadOnlyList<ProductPricingRuleInfo>> GetRulesAsync(
        Guid productVariantId,
        CancellationToken cancellationToken);

    Task<ProductPricingRuleInfo> CreateRuleAsync(
        CreateProductPricingRuleCommand command,
        CancellationToken cancellationToken);

    Task DeactivateRuleAsync(Guid ruleId, string rowVersion, CancellationToken cancellationToken);

    Task<IReadOnlyList<MarketPriceSourceInfo>> GetSourcesAsync(CancellationToken cancellationToken);

    Task<MarketPriceSourceInfo> CreateSourceAsync(
        CreateMarketPriceSourceCommand command,
        CancellationToken cancellationToken);

    Task<MarketPriceInfo> GetLatestMarketPriceAsync(
        MarketPriceType priceType,
        CancellationToken cancellationToken);

    Task<CalculatedProductPriceInfo> CalculateAsync(
        CalculateProductPriceCommand command,
        CancellationToken cancellationToken);
}
