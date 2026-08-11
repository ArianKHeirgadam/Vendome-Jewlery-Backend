namespace GoldInvoice.Infrastructure.Configuration;

public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    public int BatchSize { get; set; } = 50;

    public int PollIntervalMilliseconds { get; set; } = 1000;

    public int LockDurationSeconds { get; set; } = 60;

    public int HeartbeatIntervalSeconds { get; set; } = 20;

    public int MaximumAttempts { get; set; } = 5;

    public int RetryBaseDelaySeconds { get; set; } = 5;

    public int MaximumRetryDelaySeconds { get; set; } = 300;

    public static bool IsValid(OutboxOptions options) =>
        options.BatchSize is >= 1 and <= 500 &&
        options.PollIntervalMilliseconds is >= 100 and <= 60_000 &&
        options.LockDurationSeconds is >= 10 and <= 900 &&
        options.HeartbeatIntervalSeconds is >= 1 &&
        options.HeartbeatIntervalSeconds < options.LockDurationSeconds &&
        options.MaximumAttempts is >= 1 and <= 100 &&
        options.RetryBaseDelaySeconds is >= 1 and <= 3600 &&
        options.MaximumRetryDelaySeconds >= options.RetryBaseDelaySeconds &&
        options.MaximumRetryDelaySeconds <= 86_400;
}
