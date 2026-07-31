using GoldInvoice.Application.Common;
using GoldInvoice.Domain.Catalog;
using GoldInvoice.Domain.Pricing;

namespace GoldInvoice.Application.Pricing;

public sealed record ProductPriceCalculationInput(
    PricingMethod PricingMethod,
    MarketPriceType? MarketPriceType,
    long? FixedPriceRials,
    long? FixedGoldPricePerGramRials,
    ManufacturingWageType WageType,
    decimal WageValue,
    decimal ProfitPercentage,
    decimal TaxPercentage,
    decimal GrossWeight,
    decimal NetGoldWeight,
    int Karat,
    long? MarketSellPriceRials);

public sealed record ProductPriceCalculationResult(
    long MarketUnitPriceRials,
    long GoldValueRials,
    long WageRials,
    long ProfitRials,
    long TaxRials,
    long FinalPriceRials,
    string RoundingPolicy);

public interface IProductPriceCalculator
{
    ProductPriceCalculationResult Calculate(ProductPriceCalculationInput input);
}

public sealed class ProductPriceCalculator : IProductPriceCalculator
{
    public const string RoundingPolicyName = "WholeRialAwayFromZero";

    public ProductPriceCalculationResult Calculate(ProductPriceCalculationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (!GoldProductDetail.IsSupportedKarat(input.Karat) ||
            input.GrossWeight <= 0 ||
            input.NetGoldWeight <= 0 ||
            input.NetGoldWeight > input.GrossWeight ||
            !IsValidWage(input.WageType, input.WageValue) ||
            input.ProfitPercentage is < 0 or > 100 ||
            input.TaxPercentage is < 0 or > 100)
        {
            throw new ArgumentException("Price calculation inputs are invalid.", nameof(input));
        }

        if (input.PricingMethod == PricingMethod.ManualReview)
        {
            throw new ManualPriceReviewRequiredException();
        }

        if (input.PricingMethod == PricingMethod.FixedPrice)
        {
            if (input.FixedPriceRials is null or <= 0)
            {
                throw new ArgumentException("A fixed price is required.", nameof(input));
            }

            return new ProductPriceCalculationResult(
                MarketUnitPriceRials: 0,
                GoldValueRials: input.FixedPriceRials.Value,
                WageRials: 0,
                ProfitRials: 0,
                TaxRials: 0,
                FinalPriceRials: input.FixedPriceRials.Value,
                RoundingPolicyName);
        }

        var marketUnitPriceRials = ResolveGoldUnitPrice(input);
        var referenceKarat = input.PricingMethod == PricingMethod.MarketBased
            ? GetReferenceKarat(input.MarketPriceType)
            : input.Karat;
        var goldValue = RoundRials(
            marketUnitPriceRials * input.NetGoldWeight * input.Karat / referenceKarat);
        var wage = input.WageType switch
        {
            ManufacturingWageType.FixedRials => RoundRials(input.WageValue),
            ManufacturingWageType.PerGramRials => RoundRials(input.WageValue * input.GrossWeight),
            ManufacturingWageType.PercentageOfGoldValue =>
                RoundRials(goldValue * input.WageValue / 100m),
            _ => throw new ArgumentOutOfRangeException(nameof(input))
        };
        var profit = RoundRials((goldValue + wage) * input.ProfitPercentage / 100m);

        // The current Phase 4 policy taxes the service portion (wage plus profit).
        // The rule is isolated here so a future business-policy version can replace it.
        var tax = RoundRials((wage + profit) * input.TaxPercentage / 100m);
        var finalPrice = checked(goldValue + wage + profit + tax);

        return new ProductPriceCalculationResult(
            marketUnitPriceRials,
            goldValue,
            wage,
            profit,
            tax,
            finalPrice,
            RoundingPolicyName);
    }

    private static long ResolveGoldUnitPrice(ProductPriceCalculationInput input) => input.PricingMethod switch
    {
        PricingMethod.WeightBased when input.FixedGoldPricePerGramRials is > 0 =>
            input.FixedGoldPricePerGramRials.Value,
        PricingMethod.MarketBased when input.MarketSellPriceRials is > 0 =>
            input.MarketSellPriceRials.Value,
        PricingMethod.WeightBased =>
            throw new ArgumentException("A fixed per-gram gold price is required.", nameof(input)),
        PricingMethod.MarketBased =>
            throw new ArgumentException("A valid market sell price is required.", nameof(input)),
        _ => throw new ArgumentOutOfRangeException(nameof(input))
    };

    private static int GetReferenceKarat(MarketPriceType? priceType) => priceType switch
    {
        MarketPriceType.Gold18K => 18,
        MarketPriceType.Gold24K => 24,
        _ => throw new ArgumentException("The market quote must be an 18K or 24K gold quote.", nameof(priceType))
    };

    private static bool IsValidWage(ManufacturingWageType wageType, decimal wageValue) =>
        wageType switch
        {
            ManufacturingWageType.FixedRials or ManufacturingWageType.PerGramRials =>
                wageValue >= 0 &&
                wageValue <= long.MaxValue &&
                wageValue == decimal.Truncate(wageValue),
            ManufacturingWageType.PercentageOfGoldValue => wageValue is >= 0 and <= 100,
            _ => false
        };

    private static long RoundRials(decimal value) =>
        checked((long)decimal.Round(value, 0, MidpointRounding.AwayFromZero));
}
