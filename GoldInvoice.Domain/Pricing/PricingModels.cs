using GoldInvoice.Domain.Catalog;
using GoldInvoice.Domain.Common;

namespace GoldInvoice.Domain.Pricing;

public enum PricingMethod
{
    FixedPrice,
    WeightBased,
    MarketBased,
    ManualReview
}

public enum MarketPriceType
{
    Gold18K,
    Gold24K,
    Silver,
    Coin,
    Currency
}

public enum MarketPriceValidationStatus
{
    Accepted,
    NonPositive,
    BuyPriceAboveSellPrice,
    Stale,
    FutureDated,
    InvalidPayload
}

public sealed class ProductPricingRule : AuditableEntity
{
    private ProductPricingRule()
    {
    }

    public ProductPricingRule(
        Guid productVariantId,
        PricingMethod pricingMethod,
        MarketPriceType? goldMarketPriceType,
        long? fixedPriceRials,
        long? fixedGoldPricePerGramRials,
        ManufacturingWageType wageType,
        decimal wageValue,
        decimal profitPercentage,
        decimal taxPercentage,
        DateTimeOffset effectiveFrom,
        DateTimeOffset? effectiveTo = null)
    {
        Guard.AgainstEmpty(productVariantId, nameof(productVariantId));
        Guard.AgainstDefault(effectiveFrom, nameof(effectiveFrom));
        Guard.AgainstPercentage(profitPercentage, nameof(profitPercentage));
        Guard.AgainstPercentage(taxPercentage, nameof(taxPercentage));

        if (effectiveTo is not null && effectiveTo <= effectiveFrom)
        {
            throw new ArgumentOutOfRangeException(nameof(effectiveTo), "The end of a pricing window must follow its start.");
        }

        if (fixedPriceRials is < 0 || fixedGoldPricePerGramRials is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fixedPriceRials), "Rial amounts cannot be negative.");
        }

        var (wageAmountRials, wagePercentage) = ResolveWageValue(wageType, wageValue);

        ValidateMethodInputs(
            pricingMethod,
            goldMarketPriceType,
            fixedPriceRials,
            fixedGoldPricePerGramRials);

        ProductVariantId = productVariantId;
        PricingMethod = pricingMethod;
        GoldMarketPriceType = goldMarketPriceType;
        FixedPriceRials = fixedPriceRials;
        FixedGoldPricePerGramRials = fixedGoldPricePerGramRials;
        WageType = wageType;
        WageAmountRials = wageAmountRials;
        WagePercentage = wagePercentage;
        ProfitPercentage = profitPercentage;
        TaxPercentage = taxPercentage;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
    }

    public Guid ProductVariantId { get; private set; }

    public PricingMethod PricingMethod { get; private set; }

    public MarketPriceType? GoldMarketPriceType { get; private set; }

    public long? FixedPriceRials { get; private set; }

    public long? FixedGoldPricePerGramRials { get; private set; }

    public ManufacturingWageType WageType { get; private set; }

    public long? WageAmountRials { get; private set; }

    public decimal? WagePercentage { get; private set; }

    public decimal WageValue => WageAmountRials is long amountRials
        ? amountRials
        : WagePercentage ?? 0m;

    public decimal ProfitPercentage { get; private set; }

    public decimal TaxPercentage { get; private set; }

    public DateTimeOffset EffectiveFrom { get; private set; }

    public DateTimeOffset? EffectiveTo { get; private set; }

    public bool IsActive { get; private set; } = true;

    public bool IsEffectiveAt(DateTimeOffset timestamp) =>
        IsActive && EffectiveFrom <= timestamp && (EffectiveTo is null || EffectiveTo > timestamp);

    public void Deactivate() => IsActive = false;

    private static (long? AmountRials, decimal? Percentage) ResolveWageValue(
        ManufacturingWageType wageType,
        decimal wageValue)
    {
        Guard.AgainstNegative(wageValue, nameof(wageValue));
        return wageType switch
        {
            ManufacturingWageType.FixedRials or ManufacturingWageType.PerGramRials
                when wageValue == decimal.Truncate(wageValue) && wageValue <= long.MaxValue =>
                (checked((long)wageValue), null),
            ManufacturingWageType.FixedRials or ManufacturingWageType.PerGramRials =>
                throw new ArgumentException("Rial wage values must be whole bigint values.", nameof(wageValue)),
            ManufacturingWageType.PercentageOfGoldValue when wageValue <= 100m =>
                (null, wageValue),
            ManufacturingWageType.PercentageOfGoldValue =>
                throw new ArgumentOutOfRangeException(nameof(wageValue), "A wage percentage cannot exceed 100."),
            _ => throw new ArgumentOutOfRangeException(nameof(wageType))
        };
    }

    private static void ValidateMethodInputs(
        PricingMethod pricingMethod,
        MarketPriceType? goldMarketPriceType,
        long? fixedPriceRials,
        long? fixedGoldPricePerGramRials)
    {
        switch (pricingMethod)
        {
            case PricingMethod.FixedPrice when fixedPriceRials is null or <= 0:
                throw new ArgumentException("Fixed pricing requires a positive fixed price.", nameof(fixedPriceRials));
            case PricingMethod.WeightBased when fixedGoldPricePerGramRials is null or <= 0:
                throw new ArgumentException(
                    "Weight-based pricing requires a positive fixed gold price per gram.",
                    nameof(fixedGoldPricePerGramRials));
            case PricingMethod.MarketBased when
                goldMarketPriceType != MarketPriceType.Gold18K &&
                goldMarketPriceType != MarketPriceType.Gold24K:
                throw new ArgumentException("Market pricing requires an 18K or 24K gold price type.", nameof(goldMarketPriceType));
            case PricingMethod.ManualReview:
                break;
            case not (PricingMethod.FixedPrice or PricingMethod.WeightBased or PricingMethod.MarketBased):
                throw new ArgumentOutOfRangeException(nameof(pricingMethod));
        }
    }
}

public sealed class MarketPriceSource : AuditableEntity
{
    private MarketPriceSource()
    {
    }

    public MarketPriceSource(
        string name,
        string providerCode,
        int priority,
        string? baseUrl = null,
        string? configurationReference = null)
    {
        Guard.AgainstNegative(priority, nameof(priority));
        Name = Guard.Required(name, nameof(name), 200);
        ProviderCode = Guard.Required(providerCode, nameof(providerCode), 100).ToUpperInvariant();
        Priority = priority;
        BaseUrl = Guard.Optional(baseUrl, nameof(baseUrl), 500);
        ConfigurationReference = Guard.Optional(configurationReference, nameof(configurationReference), 500);
    }

    public string Name { get; private set; } = string.Empty;

    public string ProviderCode { get; private set; } = string.Empty;

    public bool IsActive { get; private set; } = true;

    public int Priority { get; private set; }

    public string? BaseUrl { get; private set; }

    public string? ConfigurationReference { get; private set; }

    public DateTimeOffset? LastSuccessfulFetchAt { get; private set; }

    public DateTimeOffset? LastFailureAt { get; private set; }

    public void Update(
        string name,
        int priority,
        string? baseUrl,
        string? configurationReference,
        bool isActive)
    {
        Guard.AgainstNegative(priority, nameof(priority));
        Name = Guard.Required(name, nameof(name), 200);
        Priority = priority;
        BaseUrl = Guard.Optional(baseUrl, nameof(baseUrl), 500);
        ConfigurationReference = Guard.Optional(configurationReference, nameof(configurationReference), 500);
        IsActive = isActive;
    }

    public void RecordSuccess(DateTimeOffset occurredAt)
    {
        Guard.AgainstDefault(occurredAt, nameof(occurredAt));
        LastSuccessfulFetchAt = occurredAt;
    }

    public void RecordFailure(DateTimeOffset occurredAt)
    {
        Guard.AgainstDefault(occurredAt, nameof(occurredAt));
        LastFailureAt = occurredAt;
    }
}

public sealed class MarketPriceSnapshot : AuditableEntity, IAppendOnlyEntity, IProtectedFromHardDelete
{
    private MarketPriceSnapshot()
    {
    }

    public MarketPriceSnapshot(
        Guid sourceId,
        MarketPriceType priceType,
        long buyPriceRials,
        long sellPriceRials,
        DateTimeOffset capturedAt,
        DateTimeOffset? providerTimestamp,
        bool isValid,
        MarketPriceValidationStatus validationStatus,
        string rawPayloadHash)
    {
        Guard.AgainstEmpty(sourceId, nameof(sourceId));
        Guard.AgainstNegative(buyPriceRials, nameof(buyPriceRials));
        Guard.AgainstNegative(sellPriceRials, nameof(sellPriceRials));
        Guard.AgainstDefault(capturedAt, nameof(capturedAt));

        if (providerTimestamp.HasValue && providerTimestamp.Value == default)
        {
            throw new ArgumentException("Provider timestamp cannot be the default value.", nameof(providerTimestamp));
        }

        if (isValid &&
            (validationStatus != MarketPriceValidationStatus.Accepted ||
             buyPriceRials <= 0 ||
             sellPriceRials <= 0 ||
             buyPriceRials > sellPriceRials))
        {
            throw new ArgumentException("An accepted market price must contain a valid positive spread.", nameof(isValid));
        }

        if (!isValid && validationStatus == MarketPriceValidationStatus.Accepted)
        {
            throw new ArgumentException("An invalid market price requires a rejection status.", nameof(validationStatus));
        }

        SourceId = sourceId;
        PriceType = priceType;
        BuyPriceRials = buyPriceRials;
        SellPriceRials = sellPriceRials;
        CapturedAt = capturedAt;
        ProviderTimestamp = providerTimestamp;
        IsValid = isValid;
        ValidationStatus = validationStatus;
        RawPayloadHash = Guard.Required(rawPayloadHash, nameof(rawPayloadHash), 128).ToUpperInvariant();
    }

    public Guid SourceId { get; private set; }

    public MarketPriceType PriceType { get; private set; }

    public long BuyPriceRials { get; private set; }

    public long SellPriceRials { get; private set; }

    public DateTimeOffset CapturedAt { get; private set; }

    public DateTimeOffset? ProviderTimestamp { get; private set; }

    public bool IsValid { get; private set; }

    public MarketPriceValidationStatus ValidationStatus { get; private set; }

    public string RawPayloadHash { get; private set; } = string.Empty;
}

public sealed class PriceCalculationSnapshot : AuditableEntity, IAppendOnlyEntity, IProtectedFromHardDelete
{
    private PriceCalculationSnapshot()
    {
    }

    public PriceCalculationSnapshot(
        Guid productVariantId,
        Guid pricingRuleId,
        Guid? marketPriceSnapshotId,
        PricingMethod pricingMethod,
        decimal grossWeight,
        decimal netGoldWeight,
        int karat,
        long marketUnitPriceRials,
        long goldValueRials,
        long wageRials,
        long profitRials,
        long taxRials,
        long finalPriceRials,
        DateTimeOffset calculatedAt,
        string roundingPolicy)
    {
        Guard.AgainstEmpty(productVariantId, nameof(productVariantId));
        Guard.AgainstEmpty(pricingRuleId, nameof(pricingRuleId));
        Guard.AgainstNonPositive(grossWeight, nameof(grossWeight));
        Guard.AgainstNonPositive(netGoldWeight, nameof(netGoldWeight));
        Guard.AgainstNegative(marketUnitPriceRials, nameof(marketUnitPriceRials));
        Guard.AgainstNegative(goldValueRials, nameof(goldValueRials));
        Guard.AgainstNegative(wageRials, nameof(wageRials));
        Guard.AgainstNegative(profitRials, nameof(profitRials));
        Guard.AgainstNegative(taxRials, nameof(taxRials));
        Guard.AgainstNegative(finalPriceRials, nameof(finalPriceRials));
        Guard.AgainstDefault(calculatedAt, nameof(calculatedAt));

        if (!GoldProductDetail.IsSupportedKarat(karat))
        {
            throw new ArgumentOutOfRangeException(nameof(karat));
        }

        if (checked(goldValueRials + wageRials + profitRials + taxRials) != finalPriceRials)
        {
            throw new ArgumentException("The final price must equal its stored components.", nameof(finalPriceRials));
        }

        ProductVariantId = productVariantId;
        PricingRuleId = pricingRuleId;
        MarketPriceSnapshotId = marketPriceSnapshotId;
        PricingMethod = pricingMethod;
        GrossWeight = grossWeight;
        NetGoldWeight = netGoldWeight;
        Karat = karat;
        MarketUnitPriceRials = marketUnitPriceRials;
        GoldValueRials = goldValueRials;
        WageRials = wageRials;
        ProfitRials = profitRials;
        TaxRials = taxRials;
        FinalPriceRials = finalPriceRials;
        CalculatedAt = calculatedAt;
        RoundingPolicy = Guard.Required(roundingPolicy, nameof(roundingPolicy), 100);
    }

    public Guid ProductVariantId { get; private set; }

    public Guid PricingRuleId { get; private set; }

    public Guid? MarketPriceSnapshotId { get; private set; }

    public PricingMethod PricingMethod { get; private set; }

    public decimal GrossWeight { get; private set; }

    public decimal NetGoldWeight { get; private set; }

    public int Karat { get; private set; }

    public long MarketUnitPriceRials { get; private set; }

    public long GoldValueRials { get; private set; }

    public long WageRials { get; private set; }

    public long ProfitRials { get; private set; }

    public long TaxRials { get; private set; }

    public long FinalPriceRials { get; private set; }

    public DateTimeOffset CalculatedAt { get; private set; }

    public string RoundingPolicy { get; private set; } = string.Empty;
}
