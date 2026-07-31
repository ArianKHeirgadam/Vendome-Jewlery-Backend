using System.Data;
using GoldInvoice.Application.Common;
using GoldInvoice.Application.Pricing;
using GoldInvoice.Domain.Pricing;
using GoldInvoice.Infrastructure.Configuration;
using GoldInvoice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace GoldInvoice.Infrastructure.Pricing;

internal sealed class ProductPricingService(
    GoldInvoiceDbContext dbContext,
    IProductPriceCalculator calculator,
    IOptions<MarketPriceOptions> marketPriceOptions,
    TimeProvider timeProvider) : IProductPricingService
{
    public async Task<IReadOnlyList<ProductPricingRuleInfo>> GetRulesAsync(
        Guid productVariantId,
        CancellationToken cancellationToken)
    {
        var variantExists = await dbContext.ProductVariants
            .AnyAsync(variant => variant.Id == productVariantId, cancellationToken);
        if (!variantExists)
        {
            throw new ApplicationResourceNotFoundException();
        }

        return (await dbContext.ProductPricingRules
                .AsNoTracking()
                .Where(rule => rule.ProductVariantId == productVariantId)
                .OrderByDescending(rule => rule.EffectiveFrom)
                .ToListAsync(cancellationToken))
            .Select(MapRule)
            .ToArray();
    }

    public async Task<ProductPricingRuleInfo> CreateRuleAsync(
        CreateProductPricingRuleCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        await using var transaction = await BeginSerializableTransactionAsync(cancellationToken);
        var variantExists = await (
                from detail in dbContext.GoldProductDetails
                join variant in dbContext.ProductVariants
                    on detail.ProductVariantId equals variant.Id
                join product in dbContext.Products
                    on variant.ProductId equals product.Id
                where detail.ProductVariantId == command.ProductVariantId &&
                    variant.IsActive &&
                    product.IsActive
                select detail.Id)
            .AnyAsync(cancellationToken);
        if (!variantExists)
        {
            throw new ApplicationResourceNotFoundException();
        }

        var overlaps = await dbContext.ProductPricingRules.AnyAsync(
            rule => rule.ProductVariantId == command.ProductVariantId &&
                rule.IsActive &&
                (rule.EffectiveTo == null || rule.EffectiveTo > command.EffectiveFrom) &&
                (command.EffectiveTo == null || rule.EffectiveFrom < command.EffectiveTo),
            cancellationToken);
        if (overlaps)
        {
            throw new ApplicationConflictException();
        }

        var rule = new ProductPricingRule(
            command.ProductVariantId,
            command.PricingMethod,
            command.GoldMarketPriceType,
            command.FixedPriceRials,
            command.FixedGoldPricePerGramRials,
            command.WageType,
            command.WageValue,
            command.ProfitPercentage,
            command.TaxPercentage,
            command.EffectiveFrom,
            command.EffectiveTo);
        dbContext.ProductPricingRules.Add(rule);
        await SaveChangesAsync(cancellationToken);
        await CommitAsync(transaction, cancellationToken);
        return MapRule(rule);
    }

    public async Task DeactivateRuleAsync(
        Guid ruleId,
        string rowVersion,
        CancellationToken cancellationToken)
    {
        var rule = await dbContext.ProductPricingRules.FindAsync([ruleId], cancellationToken) ??
            throw new ApplicationResourceNotFoundException();
        SetOriginalRowVersion(rule, rowVersion);
        rule.Deactivate();
        await SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MarketPriceSourceInfo>> GetSourcesAsync(
        CancellationToken cancellationToken) =>
        (await dbContext.MarketPriceSources
                .AsNoTracking()
                .OrderBy(source => source.Priority)
                .ThenBy(source => source.Name)
                .ToListAsync(cancellationToken))
            .Select(MapSource)
            .ToArray();

    public async Task<MarketPriceSourceInfo> CreateSourceAsync(
        CreateMarketPriceSourceCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var source = new MarketPriceSource(
            command.Name,
            command.ProviderCode,
            command.Priority,
            command.BaseUrl,
            command.ConfigurationReference);
        dbContext.MarketPriceSources.Add(source);
        await SaveChangesAsync(cancellationToken);
        return MapSource(source);
    }

    public async Task<MarketPriceInfo> GetLatestMarketPriceAsync(
        MarketPriceType priceType,
        CancellationToken cancellationToken)
    {
        var snapshot = await GetLatestSnapshotAsync(priceType, timeProvider.GetUtcNow(), cancellationToken) ??
            throw new ApplicationResourceNotFoundException();
        return MapMarketPrice(snapshot);
    }

    public async Task<CalculatedProductPriceInfo> CalculateAsync(
        CalculateProductPriceCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var calculatedAt = timeProvider.GetUtcNow();
        var detail = await (
                from candidate in dbContext.GoldProductDetails
                join variant in dbContext.ProductVariants
                    on candidate.ProductVariantId equals variant.Id
                join product in dbContext.Products
                    on variant.ProductId equals product.Id
                where candidate.ProductVariantId == command.ProductVariantId &&
                    variant.IsActive &&
                    product.IsActive
                select candidate)
            .SingleOrDefaultAsync(cancellationToken) ??
            throw new ApplicationResourceNotFoundException();
        var rules = await dbContext.ProductPricingRules
            .Where(rule => rule.ProductVariantId == command.ProductVariantId &&
                rule.IsActive &&
                rule.EffectiveFrom <= calculatedAt &&
                (rule.EffectiveTo == null || rule.EffectiveTo > calculatedAt))
            .OrderByDescending(rule => rule.EffectiveFrom)
            .Take(2)
            .ToListAsync(cancellationToken);
        if (rules.Count == 0)
        {
            throw new ApplicationResourceNotFoundException();
        }

        if (rules.Count > 1)
        {
            throw new ApplicationConflictException();
        }

        var rule = rules[0];
        var (grossWeight, netGoldWeight) = ResolveWeights(command, detail.IsWeightVariable, detail.GrossWeight, detail.NetGoldWeight);

        MarketPriceSnapshot? marketSnapshot = null;
        if (rule.PricingMethod == PricingMethod.MarketBased)
        {
            marketSnapshot = await GetLatestSnapshotAsync(
                rule.GoldMarketPriceType ?? throw new InvalidOperationException("The pricing rule has no market price type."),
                calculatedAt,
                cancellationToken) ?? throw new ApplicationResourceNotFoundException();
        }

        var result = calculator.Calculate(new ProductPriceCalculationInput(
            rule.PricingMethod,
            rule.GoldMarketPriceType,
            rule.FixedPriceRials,
            rule.FixedGoldPricePerGramRials,
            rule.WageType,
            rule.WageValue,
            rule.ProfitPercentage,
            rule.TaxPercentage,
            grossWeight,
            netGoldWeight,
            detail.Karat,
            marketSnapshot?.SellPriceRials));

        var snapshot = new PriceCalculationSnapshot(
            command.ProductVariantId,
            rule.Id,
            marketSnapshot?.Id,
            rule.PricingMethod,
            grossWeight,
            netGoldWeight,
            detail.Karat,
            result.MarketUnitPriceRials,
            result.GoldValueRials,
            result.WageRials,
            result.ProfitRials,
            result.TaxRials,
            result.FinalPriceRials,
            calculatedAt,
            result.RoundingPolicy);
        dbContext.PriceCalculationSnapshots.Add(snapshot);
        await SaveChangesAsync(cancellationToken);

        return new CalculatedProductPriceInfo(
            snapshot.Id,
            snapshot.ProductVariantId,
            snapshot.PricingRuleId,
            snapshot.MarketPriceSnapshotId,
            snapshot.MarketUnitPriceRials,
            snapshot.GoldValueRials,
            snapshot.WageRials,
            snapshot.ProfitRials,
            snapshot.TaxRials,
            snapshot.FinalPriceRials,
            snapshot.CalculatedAt,
            snapshot.RoundingPolicy);
    }

    private async Task<MarketPriceSnapshot?> GetLatestSnapshotAsync(
        MarketPriceType priceType,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        var oldestAccepted = at.AddMinutes(-marketPriceOptions.Value.MaximumQuoteAgeMinutes);
        return await (
                from snapshot in dbContext.MarketPriceSnapshots
                join source in dbContext.MarketPriceSources on snapshot.SourceId equals source.Id
                where source.IsActive &&
                    snapshot.PriceType == priceType &&
                    snapshot.IsValid &&
                    snapshot.CapturedAt <= at &&
                    snapshot.CapturedAt >= oldestAccepted
                orderby source.Priority, snapshot.CapturedAt descending, snapshot.Id
                select snapshot)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static (decimal GrossWeight, decimal NetGoldWeight) ResolveWeights(
        CalculateProductPriceCommand command,
        bool isWeightVariable,
        decimal catalogGrossWeight,
        decimal catalogNetGoldWeight)
    {
        if (!isWeightVariable)
        {
            if (command.ActualGrossWeight is not null || command.ActualNetGoldWeight is not null)
            {
                throw new ArgumentException("Actual weights are accepted only for variable-weight variants.", nameof(command));
            }

            return (catalogGrossWeight, catalogNetGoldWeight);
        }

        if (command.ActualGrossWeight is not > 0 ||
            command.ActualNetGoldWeight is not > 0 ||
            command.ActualNetGoldWeight > command.ActualGrossWeight)
        {
            throw new ArgumentException("Valid actual weights are required for a variable-weight variant.", nameof(command));
        }

        return (command.ActualGrossWeight.Value, command.ActualNetGoldWeight.Value);
    }

    private static ProductPricingRuleInfo MapRule(ProductPricingRule rule) => new(
        rule.Id,
        rule.ProductVariantId,
        rule.PricingMethod,
        rule.GoldMarketPriceType,
        rule.FixedPriceRials,
        rule.FixedGoldPricePerGramRials,
        rule.WageType,
        rule.WageValue,
        rule.ProfitPercentage,
        rule.TaxPercentage,
        rule.EffectiveFrom,
        rule.EffectiveTo,
        rule.IsActive,
        Convert.ToBase64String(rule.RowVersion));

    private static MarketPriceSourceInfo MapSource(MarketPriceSource source) => new(
        source.Id,
        source.Name,
        source.ProviderCode,
        source.IsActive,
        source.Priority,
        source.BaseUrl,
        source.ConfigurationReference,
        source.LastSuccessfulFetchAt,
        source.LastFailureAt,
        Convert.ToBase64String(source.RowVersion));

    private static MarketPriceInfo MapMarketPrice(MarketPriceSnapshot snapshot) => new(
        snapshot.Id,
        snapshot.SourceId,
        snapshot.PriceType,
        snapshot.BuyPriceRials,
        snapshot.SellPriceRials,
        snapshot.CapturedAt,
        snapshot.ProviderTimestamp,
        snapshot.ValidationStatus.ToString());

    private void SetOriginalRowVersion<TEntity>(TEntity entity, string value)
        where TEntity : class =>
        dbContext.Entry(entity).Property("RowVersion").OriginalValue = DecodeRowVersion(value);

    private async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ApplicationConcurrencyException();
        }
        catch (DbUpdateException)
        {
            throw new ApplicationConflictException();
        }
    }

    private async Task<IDbContextTransaction?> BeginSerializableTransactionAsync(
        CancellationToken cancellationToken) =>
        dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;

    private static async Task CommitAsync(
        IDbContextTransaction? transaction,
        CancellationToken cancellationToken)
    {
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
    }

    private static byte[] DecodeRowVersion(string value)
    {
        try
        {
            return Convert.FromBase64String(value ?? string.Empty);
        }
        catch (FormatException exception)
        {
            throw new ArgumentException("The concurrency token is invalid.", nameof(value), exception);
        }
    }
}
