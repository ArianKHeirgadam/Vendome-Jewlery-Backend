using GoldInvoice.Domain.Platform;
using GoldInvoice.Infrastructure.Configuration;
using GoldInvoice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GoldInvoice.Infrastructure.Integration;

public sealed record DataRetentionResult(
    int PurgedOutboxMessages,
    int PurgedIdempotencyRecords,
    int PurgedPaymentCallbacks);

public interface IDataRetentionService
{
    Task<DataRetentionResult> SweepAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Bounded cleanup of the long-lived event and idempotency tables.
///
/// - OutboxMessages: Processed and DeadLetter rows older than the configured
///   retention window. In-flight rows (Pending, Failed, Processing) are never
///   touched.
/// - IdempotencyRecords: only records already Completed and past their own
///   expiry are removed; an in-flight (Pending) record is never deleted
///   because the request it belongs to would lose its deduplication guard.
/// - PaymentCallbacks: the raw callback journal beyond the retention window.
///
/// Everything runs in bounded batches so a single sweep never holds the whole
/// table in memory or in one long transaction.
/// </summary>
internal sealed class DataRetentionService(
    GoldInvoiceDbContext dbContext,
    IOptions<RetentionOptions> options,
    TimeProvider timeProvider,
    ILogger<DataRetentionService> logger) : IDataRetentionService
{
    public async Task<DataRetentionResult> SweepAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var now = timeProvider.GetUtcNow();
        var outboxCutoff = now.AddDays(-settings.OutboxProcessedRetentionDays);
        var callbackCutoff = now.AddDays(-settings.CallbackLogRetentionDays);

        var purgedOutbox = await SweepOutboxAsync(outboxCutoff, settings.MaximumBatchSize, cancellationToken);
        var purgedIdempotency = await SweepIdempotencyAsync(now, settings, cancellationToken);
        var purgedCallbacks = await SweepCallbacksAsync(callbackCutoff, settings.MaximumBatchSize, cancellationToken);

        if (purgedOutbox > 0 || purgedIdempotency > 0 || purgedCallbacks > 0)
        {
            logger.LogInformation(
                "Data-retention sweep removed {Outbox} outbox, {Idempotency} idempotency, and {Callbacks} callback rows",
                purgedOutbox,
                purgedIdempotency,
                purgedCallbacks);
        }

        return new DataRetentionResult(purgedOutbox, purgedIdempotency, purgedCallbacks);
    }

    private async Task<int> SweepOutboxAsync(
        DateTimeOffset cutoff,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var total = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (dbContext.Database.IsRelational())
            {
                var deleted = await dbContext.OutboxMessages
                    .Where(message =>
                        message.ProcessedAt != null &&
                        message.ProcessedAt < cutoff &&
                        (message.Status == OutboxMessageStatus.Processed ||
                         message.Status == OutboxMessageStatus.DeadLetter))
                    .OrderBy(message => message.ProcessedAt)
                    .Take(batchSize)
                    .ExecuteDeleteAsync(cancellationToken);
                total += deleted;
                if (deleted < batchSize)
                {
                    return total;
                }
            }
            else
            {
                var candidates = await dbContext.OutboxMessages
                    .Where(message =>
                        message.ProcessedAt != null &&
                        message.ProcessedAt < cutoff &&
                        (message.Status == OutboxMessageStatus.Processed ||
                         message.Status == OutboxMessageStatus.DeadLetter))
                    .OrderBy(message => message.ProcessedAt)
                    .Take(batchSize)
                    .ToListAsync(cancellationToken);
                if (candidates.Count == 0)
                {
                    return total;
                }

                dbContext.OutboxMessages.RemoveRange(candidates);
                await dbContext.SaveChangesAsync(cancellationToken);
                total += candidates.Count;
            }
        }
    }

    private async Task<int> SweepIdempotencyAsync(
        DateTimeOffset now,
        RetentionOptions settings,
        CancellationToken cancellationToken)
    {
        if (!settings.PurgeExpiredIdempotencyRecords)
        {
            return 0;
        }

        if (dbContext.Database.IsRelational())
        {
            return await dbContext.IdempotencyRecords
                .Where(record =>
                    record.Status == IdempotencyRecordStatus.Completed &&
                    record.ExpiresAt < now)
                .OrderBy(record => record.ExpiresAt)
                .Take(settings.MaximumBatchSize)
                .ExecuteDeleteAsync(cancellationToken);
        }

        var expired = await dbContext.IdempotencyRecords
            .Where(record =>
                record.Status == IdempotencyRecordStatus.Completed &&
                record.ExpiresAt < now)
            .OrderBy(record => record.ExpiresAt)
            .Take(settings.MaximumBatchSize)
            .ToListAsync(cancellationToken);
        if (expired.Count > 0)
        {
            dbContext.IdempotencyRecords.RemoveRange(expired);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return expired.Count;
    }

    private async Task<int> SweepCallbacksAsync(
        DateTimeOffset cutoff,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var total = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (dbContext.Database.IsRelational())
            {
                var deleted = await dbContext.PaymentCallbacks
                    .Where(callback => callback.ReceivedAt < cutoff)
                    .OrderBy(callback => callback.ReceivedAt)
                    .Take(batchSize)
                    .ExecuteDeleteAsync(cancellationToken);
                total += deleted;
                if (deleted < batchSize)
                {
                    return total;
                }
            }
            else
            {
                var candidates = await dbContext.PaymentCallbacks
                    .Where(callback => callback.ReceivedAt < cutoff)
                    .OrderBy(callback => callback.ReceivedAt)
                    .Take(batchSize)
                    .ToListAsync(cancellationToken);
                if (candidates.Count == 0)
                {
                    return total;
                }

                dbContext.PaymentCallbacks.RemoveRange(candidates);
                await dbContext.SaveChangesAsync(cancellationToken);
                total += candidates.Count;
            }
        }
    }
}