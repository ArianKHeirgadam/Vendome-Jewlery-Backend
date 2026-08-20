namespace GoldInvoice.Infrastructure.Configuration;

public sealed class RetentionOptions
{
    public const string SectionName = "DataRetention";

    public int OutboxProcessedRetentionDays { get; set; } = 30;

    public int CallbackLogRetentionDays { get; set; } = 90;

    public bool PurgeExpiredIdempotencyRecords { get; set; } = true;

    public int SweepIntervalHours { get; set; } = 24;

    public int MaximumBatchSize { get; set; } = 1000;

    public static bool IsValid(RetentionOptions options) =>
        options.OutboxProcessedRetentionDays is >= 1 and <= 3650 &&
        options.CallbackLogRetentionDays is >= 1 and <= 3650 &&
        options.SweepIntervalHours is >= 1 and <= 24 * 31 &&
        options.MaximumBatchSize is >= 100 and <= 10_000;
}