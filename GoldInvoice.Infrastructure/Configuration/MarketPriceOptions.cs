namespace GoldInvoice.Infrastructure.Configuration;

public sealed class MarketPriceOptions
{
    public const string SectionName = "MarketPrices";

    public int PollIntervalMinutes { get; init; } = 5;

    public int ProviderTimeoutSeconds { get; init; } = 10;

    public int RetryCount { get; init; } = 3;

    public int RetryBaseDelayMilliseconds { get; init; } = 250;

    public int MaximumQuoteAgeMinutes { get; init; } = 30;

    public int MaximumFutureClockSkewSeconds { get; init; } = 60;

    public static bool IsValid(MarketPriceOptions options) =>
        options.PollIntervalMinutes is >= 1 and <= 1440 &&
        options.ProviderTimeoutSeconds is >= 1 and <= 120 &&
        options.RetryCount is >= 1 and <= 5 &&
        options.RetryBaseDelayMilliseconds is >= 10 and <= 10_000 &&
        options.MaximumQuoteAgeMinutes is >= 1 and <= 1440 &&
        options.MaximumFutureClockSkewSeconds is >= 0 and <= 600;
}
