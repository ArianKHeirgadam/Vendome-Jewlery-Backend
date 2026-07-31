using System.Security.Cryptography;
using System.Text;
using GoldInvoice.Application.Common;
using GoldInvoice.Application.Pricing;
using GoldInvoice.Domain.Pricing;
using GoldInvoice.Infrastructure.Configuration;
using GoldInvoice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GoldInvoice.Infrastructure.Pricing;

internal sealed partial class MarketPriceIngestionService(
    GoldInvoiceDbContext dbContext,
    IEnumerable<IMarketPriceProvider> providers,
    IOptions<MarketPriceOptions> options,
    TimeProvider timeProvider,
    ILogger<MarketPriceIngestionService> logger) : IMarketPriceIngestionService
{
    public async Task<int> PollAllAsync(CancellationToken cancellationToken)
    {
        var registeredProviderCodes = providers
            .Select(provider => provider.ProviderCode.Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (registeredProviderCodes.Length == 0)
        {
            return 0;
        }

        var activeProviderCodes = await dbContext.MarketPriceSources
            .AsNoTracking()
            .Where(source => source.IsActive && registeredProviderCodes.Contains(source.ProviderCode))
            .OrderBy(source => source.Priority)
            .Select(source => source.ProviderCode)
            .ToListAsync(cancellationToken);
        var total = 0;
        foreach (var providerCode in activeProviderCodes)
        {
            try
            {
                total += await PollSourceAsync(providerCode, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (MarketPriceProviderUnavailableException)
            {
                // PollSourceAsync already records source health and a sanitized structured event.
                // One unavailable provider must not block the remaining registered sources.
            }
        }

        return total;
    }

    public async Task<int> PollSourceAsync(string providerCode, CancellationToken cancellationToken)
    {
        var normalizedCode = string.IsNullOrWhiteSpace(providerCode)
            ? throw new ArgumentException("A provider code is required.", nameof(providerCode))
            : providerCode.Trim().ToUpperInvariant();
        var source = await dbContext.MarketPriceSources
            .SingleOrDefaultAsync(
                item => item.ProviderCode == normalizedCode && item.IsActive,
                cancellationToken) ?? throw new ApplicationResourceNotFoundException();
        var provider = providers.SingleOrDefault(item =>
            string.Equals(item.ProviderCode, normalizedCode, StringComparison.OrdinalIgnoreCase)) ??
            throw new ApplicationResourceNotFoundException();

        IReadOnlyList<MarketPriceQuote> quotes;
        try
        {
            quotes = await FetchWithRetryAsync(provider, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            source.RecordFailure(timeProvider.GetUtcNow());
            await dbContext.SaveChangesAsync(cancellationToken);
            ProviderFailed(logger, normalizedCode, exception.GetType().Name);
            throw new MarketPriceProviderUnavailableException(exception);
        }

        var capturedAt = timeProvider.GetUtcNow();
        var existingSnapshots = await dbContext.MarketPriceSnapshots
            .Where(snapshot => snapshot.SourceId == source.Id)
            .Select(snapshot => new { snapshot.PriceType, snapshot.RawPayloadHash })
            .ToListAsync(cancellationToken);
        var knownHashes = existingSnapshots
            .Select(snapshot => CreateQuoteKey(snapshot.PriceType, snapshot.RawPayloadHash))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = 0;

        foreach (var quote in quotes
                     .GroupBy(item => item.PriceType)
                     .Select(group => group.Last()))
        {
            var rawPayloadHash = NormalizeHash(quote, normalizedCode);
            if (!knownHashes.Add(CreateQuoteKey(quote.PriceType, rawPayloadHash)))
            {
                continue;
            }

            var validationStatus = ValidateQuote(quote, capturedAt, options.Value);
            dbContext.MarketPriceSnapshots.Add(new MarketPriceSnapshot(
                source.Id,
                quote.PriceType,
                Math.Max(0, quote.BuyPriceRials),
                Math.Max(0, quote.SellPriceRials),
                capturedAt.AddTicks(added),
                quote.ProviderTimestamp,
                validationStatus == MarketPriceValidationStatus.Accepted,
                validationStatus,
                rawPayloadHash));
            added++;
        }

        source.RecordSuccess(capturedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        ProviderSucceeded(logger, normalizedCode, added);
        return added;
    }

    private async Task<IReadOnlyList<MarketPriceQuote>> FetchWithRetryAsync(
        IMarketPriceProvider provider,
        CancellationToken cancellationToken)
    {
        Exception? lastException = null;
        for (var attempt = 1; attempt <= options.Value.RetryCount; attempt++)
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(TimeSpan.FromSeconds(options.Value.ProviderTimeoutSeconds));
            try
            {
                return await provider.FetchAsync(timeoutSource.Token);
            }
            catch (Exception exception) when (
                attempt < options.Value.RetryCount &&
                (!cancellationToken.IsCancellationRequested || exception is not OperationCanceledException))
            {
                lastException = exception;
                var delayMilliseconds = checked(
                    options.Value.RetryBaseDelayMilliseconds * (1 << (attempt - 1)));
                await Task.Delay(TimeSpan.FromMilliseconds(delayMilliseconds), cancellationToken);
            }
        }

        throw lastException ?? new InvalidOperationException("The market-price provider returned no result.");
    }

    private static MarketPriceValidationStatus ValidateQuote(
        MarketPriceQuote quote,
        DateTimeOffset capturedAt,
        MarketPriceOptions options)
    {
        if (quote.BuyPriceRials <= 0 || quote.SellPriceRials <= 0)
        {
            return MarketPriceValidationStatus.NonPositive;
        }

        if (quote.BuyPriceRials > quote.SellPriceRials)
        {
            return MarketPriceValidationStatus.BuyPriceAboveSellPrice;
        }

        if (quote.ProviderTimestamp > capturedAt.AddSeconds(options.MaximumFutureClockSkewSeconds))
        {
            return MarketPriceValidationStatus.FutureDated;
        }

        if (quote.ProviderTimestamp < capturedAt.AddMinutes(-options.MaximumQuoteAgeMinutes))
        {
            return MarketPriceValidationStatus.Stale;
        }

        return MarketPriceValidationStatus.Accepted;
    }

    private static string NormalizeHash(MarketPriceQuote quote, string providerCode)
    {
        var supplied = quote.RawPayloadHash?.Trim().ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(supplied) &&
            supplied.Length is 64 or 128 &&
            supplied.All(Uri.IsHexDigit))
        {
            return supplied;
        }

        var sanitizedIdentity = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{providerCode}|{quote.PriceType}|{quote.BuyPriceRials}|{quote.SellPriceRials}|{quote.ProviderTimestamp:O}");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sanitizedIdentity)));
    }

    private static string CreateQuoteKey(MarketPriceType priceType, string rawPayloadHash) =>
        $"{priceType}:{rawPayloadHash}";

    [LoggerMessage(
        EventId = 4100,
        Level = LogLevel.Information,
        Message = "Market-price provider {ProviderCode} stored {SnapshotCount} snapshots")]
    private static partial void ProviderSucceeded(ILogger logger, string providerCode, int snapshotCount);

    [LoggerMessage(
        EventId = 4101,
        Level = LogLevel.Warning,
        Message = "Market-price provider {ProviderCode} failed with {FailureType}")]
    private static partial void ProviderFailed(ILogger logger, string providerCode, string failureType);

    private sealed class MarketPriceProviderUnavailableException(Exception innerException)
        : Exception("The market-price provider is unavailable.", innerException)
    {
    }
}
