using GoldInvoice.Application.Pricing;
using GoldInvoice.Application.Security;
using GoldInvoice.Contracts.Pricing;
using GoldInvoice.Domain.Catalog;
using GoldInvoice.Domain.Pricing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GoldInvoice.Api.Controllers;

[ApiController]
[Authorize]
[RequestSizeLimit(32 * 1024)]
[Route("api/v1/pricing")]
public sealed class PricingController(
    IProductPricingService pricingService,
    IMarketPriceIngestionService ingestionService) : ControllerBase
{
    [Authorize(Policy = SecurityPermissions.ProductsRead)]
    [HttpGet("rules/variant/{productVariantId:guid}")]
    public async Task<ActionResult<IReadOnlyList<ProductPricingRuleResponse>>> GetRules(
        Guid productVariantId,
        CancellationToken cancellationToken) =>
        Ok((await pricingService.GetRulesAsync(productVariantId, cancellationToken))
            .Select(MapRule)
            .ToArray());

    [Authorize(Policy = SecurityPermissions.ProductsManage)]
    [HttpPost("rules")]
    public async Task<ActionResult<ProductPricingRuleResponse>> CreateRule(
        CreateProductPricingRuleRequest request,
        CancellationToken cancellationToken)
    {
        var rule = await pricingService.CreateRuleAsync(
            new CreateProductPricingRuleCommand(
                request.ProductVariantId,
                ParseEnum<PricingMethod>(request.PricingMethod),
                ParseOptionalEnum<MarketPriceType>(request.GoldMarketPriceType),
                request.FixedPriceRials,
                request.FixedGoldPricePerGramRials,
                ParseEnum<ManufacturingWageType>(request.WageType),
                request.WageValue,
                request.ProfitPercentage,
                request.TaxPercentage,
                request.EffectiveFrom,
                request.EffectiveTo),
            cancellationToken);
        return Created(
            $"/api/v1/pricing/rules/variant/{rule.ProductVariantId:D}",
            MapRule(rule));
    }

    [Authorize(Policy = SecurityPermissions.ProductsManage)]
    [HttpDelete("rules/{ruleId:guid}")]
    public async Task<IActionResult> DeactivateRule(
        Guid ruleId,
        [FromQuery] string rowVersion,
        CancellationToken cancellationToken)
    {
        await pricingService.DeactivateRuleAsync(ruleId, rowVersion, cancellationToken);
        return NoContent();
    }

    [Authorize(Policy = SecurityPermissions.ProductsManage)]
    [HttpGet("market/sources")]
    public async Task<ActionResult<IReadOnlyList<MarketPriceSourceResponse>>> GetSources(
        CancellationToken cancellationToken) =>
        Ok((await pricingService.GetSourcesAsync(cancellationToken)).Select(MapSource).ToArray());

    [Authorize(Policy = SecurityPermissions.ProductsManage)]
    [HttpPost("market/sources")]
    public async Task<ActionResult<MarketPriceSourceResponse>> CreateSource(
        CreateMarketPriceSourceRequest request,
        CancellationToken cancellationToken)
    {
        var source = await pricingService.CreateSourceAsync(
            new CreateMarketPriceSourceCommand(
                request.Name,
                request.ProviderCode,
                request.Priority,
                request.BaseUrl,
                request.ConfigurationReference),
            cancellationToken);
        return Created("/api/v1/pricing/market/sources", MapSource(source));
    }

    [Authorize(Policy = SecurityPermissions.ProductsRead)]
    [HttpGet("market/latest/{priceType}")]
    public async Task<ActionResult<MarketPriceResponse>> GetLatestMarketPrice(
        string priceType,
        CancellationToken cancellationToken) =>
        Ok(MapMarketPrice(await pricingService.GetLatestMarketPriceAsync(
            ParseEnum<MarketPriceType>(priceType),
            cancellationToken)));

    [Authorize(Policy = SecurityPermissions.ProductsManage)]
    [HttpPost("market/sources/{providerCode}/poll")]
    public async Task<ActionResult> PollSource(
        string providerCode,
        CancellationToken cancellationToken)
    {
        var count = await ingestionService.PollSourceAsync(providerCode, cancellationToken);
        return Ok(new { StoredSnapshotCount = count });
    }

    [Authorize(Policy = SecurityPermissions.ProductsRead)]
    [HttpPost("calculate")]
    public async Task<ActionResult<CalculatedProductPriceResponse>> Calculate(
        CalculateProductPriceRequest request,
        CancellationToken cancellationToken) =>
        Ok(MapCalculatedPrice(await pricingService.CalculateAsync(
            new CalculateProductPriceCommand(
                request.ProductVariantId,
                request.ActualGrossWeight,
                request.ActualNetGoldWeight),
            cancellationToken)));

    private static ProductPricingRuleResponse MapRule(ProductPricingRuleInfo rule) => new()
    {
        Id = rule.Id,
        ProductVariantId = rule.ProductVariantId,
        PricingMethod = rule.PricingMethod.ToString(),
        GoldMarketPriceType = rule.GoldMarketPriceType?.ToString(),
        FixedPriceRials = rule.FixedPriceRials,
        FixedGoldPricePerGramRials = rule.FixedGoldPricePerGramRials,
        WageType = rule.WageType.ToString(),
        WageValue = rule.WageValue,
        ProfitPercentage = rule.ProfitPercentage,
        TaxPercentage = rule.TaxPercentage,
        EffectiveFrom = rule.EffectiveFrom,
        EffectiveTo = rule.EffectiveTo,
        IsActive = rule.IsActive,
        RowVersion = rule.RowVersion
    };

    private static MarketPriceSourceResponse MapSource(MarketPriceSourceInfo source) => new()
    {
        Id = source.Id,
        Name = source.Name,
        ProviderCode = source.ProviderCode,
        IsActive = source.IsActive,
        Priority = source.Priority,
        BaseUrl = source.BaseUrl,
        ConfigurationReference = source.ConfigurationReference,
        LastSuccessfulFetchAt = source.LastSuccessfulFetchAt,
        LastFailureAt = source.LastFailureAt,
        RowVersion = source.RowVersion
    };

    private static MarketPriceResponse MapMarketPrice(MarketPriceInfo price) => new()
    {
        Id = price.Id,
        SourceId = price.SourceId,
        PriceType = price.PriceType.ToString(),
        BuyPriceRials = price.BuyPriceRials,
        SellPriceRials = price.SellPriceRials,
        CapturedAt = price.CapturedAt,
        ProviderTimestamp = price.ProviderTimestamp,
        ValidationStatus = price.ValidationStatus
    };

    private static CalculatedProductPriceResponse MapCalculatedPrice(CalculatedProductPriceInfo price) => new()
    {
        SnapshotId = price.SnapshotId,
        ProductVariantId = price.ProductVariantId,
        PricingRuleId = price.PricingRuleId,
        MarketPriceSnapshotId = price.MarketPriceSnapshotId,
        MarketUnitPriceRials = price.MarketUnitPriceRials,
        GoldValueRials = price.GoldValueRials,
        WageRials = price.WageRials,
        ProfitRials = price.ProfitRials,
        TaxRials = price.TaxRials,
        FinalPriceRials = price.FinalPriceRials,
        CalculatedAt = price.CalculatedAt,
        RoundingPolicy = price.RoundingPolicy
    };

    private static TEnum ParseEnum<TEnum>(string value)
        where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new ArgumentException($"'{value}' is not a supported {typeof(TEnum).Name} value.");

    private static TEnum? ParseOptionalEnum<TEnum>(string? value)
        where TEnum : struct, Enum =>
        string.IsNullOrWhiteSpace(value) ? null : ParseEnum<TEnum>(value);
}
