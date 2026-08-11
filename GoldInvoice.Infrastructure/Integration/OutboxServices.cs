using System.Text.Json;
using GoldInvoice.Application.Common;
using GoldInvoice.Application.Integration;
using GoldInvoice.Domain.Common;
using GoldInvoice.Domain.Platform;
using GoldInvoice.Infrastructure.Configuration;
using GoldInvoice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GoldInvoice.Infrastructure.Integration;

internal sealed record OutboxClaim(
    Guid Id,
    string MessageType,
    string Payload,
    DateTimeOffset OccurredAt,
    int RetryCount);

internal interface IOutboxStore
{
    Task<IReadOnlyList<OutboxClaim>> ClaimAsync(
        Guid lockId,
        DateTimeOffset now,
        DateTimeOffset lockedUntil,
        int batchSize,
        CancellationToken cancellationToken);

    Task<bool> RenewAsync(
        Guid messageId,
        Guid lockId,
        DateTimeOffset now,
        DateTimeOffset lockedUntil,
        CancellationToken cancellationToken);

    Task<bool> CompleteAsync(
        Guid messageId,
        Guid lockId,
        DateTimeOffset processedAt,
        CancellationToken cancellationToken);

    Task<bool> FailAsync(
        Guid messageId,
        Guid lockId,
        string failure,
        DateTimeOffset failedAt,
        DateTimeOffset? nextRetryAt,
        bool deadLetter,
        CancellationToken cancellationToken);

    Task ReleaseAsync(
        Guid messageId,
        Guid lockId,
        DateTimeOffset releasedAt,
        CancellationToken cancellationToken);
}

internal sealed class OutboxStore(GoldInvoiceDbContext dbContext) : IOutboxStore
{
    public async Task<IReadOnlyList<OutboxClaim>> ClaimAsync(
        Guid lockId,
        DateTimeOffset now,
        DateTimeOffset lockedUntil,
        int batchSize,
        CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsSqlServer())
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync($$"""
                ;WITH [Claimable] AS
                (
                    SELECT TOP ({{batchSize}}) *
                    FROM [integration].[OutboxMessages] WITH (UPDLOCK, READPAST, ROWLOCK)
                    WHERE
                        (([Status] IN ('Pending', 'Failed')) AND
                         ([NextRetryAt] IS NULL OR [NextRetryAt] <= {{now}}))
                        OR
                        ([Status] = 'Processing' AND [LockedUntil] <= {{now}})
                    ORDER BY [OccurredAt], [Id]
                )
                UPDATE [Claimable]
                SET [Status] = 'Processing',
                    [LockId] = {{lockId}},
                    [LockedUntil] = {{lockedUntil}},
                    [UpdatedAt] = {{now}};
                """, cancellationToken);
        }
        else
        {
            var candidates = await dbContext.OutboxMessages
                .Where(message =>
                    ((message.Status == OutboxMessageStatus.Pending ||
                      message.Status == OutboxMessageStatus.Failed) &&
                     (message.NextRetryAt == null || message.NextRetryAt <= now)) ||
                    (message.Status == OutboxMessageStatus.Processing && message.LockedUntil <= now))
                .OrderBy(message => message.OccurredAt)
                .ThenBy(message => message.Id)
                .Take(batchSize)
                .ToListAsync(cancellationToken);
            foreach (var message in candidates)
            {
                message.Claim(lockId, lockedUntil, now);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return await dbContext.OutboxMessages
            .AsNoTracking()
            .Where(message => message.LockId == lockId && message.Status == OutboxMessageStatus.Processing)
            .OrderBy(message => message.OccurredAt)
            .ThenBy(message => message.Id)
            .Select(message => new OutboxClaim(
                message.Id,
                message.MessageType,
                message.Payload,
                message.OccurredAt,
                message.RetryCount))
            .ToListAsync(cancellationToken);
    }

    public Task<bool> RenewAsync(
        Guid messageId,
        Guid lockId,
        DateTimeOffset now,
        DateTimeOffset lockedUntil,
        CancellationToken cancellationToken) =>
        MutateAsync(
            messageId,
            message => message.RenewLock(lockId, lockedUntil, now),
            cancellationToken);

    public Task<bool> CompleteAsync(
        Guid messageId,
        Guid lockId,
        DateTimeOffset processedAt,
        CancellationToken cancellationToken) =>
        MutateAsync(
            messageId,
            message => message.MarkProcessed(lockId, processedAt),
            cancellationToken);

    public Task<bool> FailAsync(
        Guid messageId,
        Guid lockId,
        string failure,
        DateTimeOffset failedAt,
        DateTimeOffset? nextRetryAt,
        bool deadLetter,
        CancellationToken cancellationToken) =>
        MutateAsync(
            messageId,
            message => message.MarkFailed(lockId, failure, failedAt, nextRetryAt, deadLetter),
            cancellationToken);

    public async Task ReleaseAsync(
        Guid messageId,
        Guid lockId,
        DateTimeOffset releasedAt,
        CancellationToken cancellationToken)
    {
        await MutateAsync(
            messageId,
            message => message.ReleaseClaim(lockId, releasedAt, releasedAt),
            cancellationToken);
    }

    private async Task<bool> MutateAsync(
        Guid messageId,
        Action<OutboxMessage> mutation,
        CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();
        var message = await dbContext.OutboxMessages.FindAsync([messageId], cancellationToken);
        if (message is null)
        {
            return false;
        }

        try
        {
            mutation(message);
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
        catch (DomainConflictException)
        {
            return false;
        }
    }
}

internal sealed partial class OutboxDispatcher(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxOptions> options,
    TimeProvider timeProvider,
    ILogger<OutboxDispatcher> logger) : IOutboxDispatcher
{
    public async Task<OutboxDispatchResult> DispatchBatchAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var lockId = Guid.NewGuid();
        var now = timeProvider.GetUtcNow();
        IReadOnlyList<OutboxClaim> claims;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            claims = await scope.ServiceProvider.GetRequiredService<IOutboxStore>().ClaimAsync(
                lockId,
                now,
                now.AddSeconds(settings.LockDurationSeconds),
                settings.BatchSize,
                cancellationToken);
        }

        var processed = 0;
        var failed = 0;
        for (var claimIndex = 0; claimIndex < claims.Count; claimIndex++)
        {
            var claim = claims[claimIndex];
            try
            {
                var integrationEvent = IntegrationEventSerializer.Deserialize(
                    claim.Id,
                    claim.MessageType,
                    claim.OccurredAt,
                    claim.Payload);
                await HandleWithHeartbeatAsync(integrationEvent, lockId, settings, cancellationToken);
                if (!await CompleteAsync(claim.Id, lockId, cancellationToken))
                {
                    throw new InvalidOperationException("The outbox lock was lost before completion.");
                }

                processed++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                for (var releaseIndex = claimIndex; releaseIndex < claims.Count; releaseIndex++)
                {
                    await ReleaseAsync(claims[releaseIndex].Id, lockId);
                }

                throw;
            }
            catch (Exception exception)
            {
                failed++;
                var permanent = exception is PermanentIntegrationEventException;
                var deadLetter = permanent || claim.RetryCount + 1 >= settings.MaximumAttempts;
                var failedAt = timeProvider.GetUtcNow();
                DateTimeOffset? nextRetryAt = deadLetter
                    ? null
                    : failedAt.Add(CalculateBackoff(claim.RetryCount, settings));
                await FailAsync(
                    claim.Id,
                    lockId,
                    SanitizeFailure(exception, permanent),
                    failedAt,
                    nextRetryAt,
                    deadLetter,
                    CancellationToken.None);
                DispatchFailed(logger, claim.Id, exception.GetType().Name, deadLetter);
            }
        }

        return new OutboxDispatchResult(claims.Count, processed, failed);
    }

    private async Task HandleWithHeartbeatAsync(
        ClaimedIntegrationEvent integrationEvent,
        Guid lockId,
        OutboxOptions settings,
        CancellationToken cancellationToken)
    {
        await using var handlerScope = scopeFactory.CreateAsyncScope();
        var handlers = handlerScope.ServiceProvider.GetServices<IIntegrationEventHandler>().ToArray();
        if (handlers.Length == 0)
        {
            throw new PermanentIntegrationEventException("No integration-event handler is registered.");
        }

        using var handlerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var handlingTask = Task.WhenAll(handlers.Select(handler =>
            handler.HandleAsync(integrationEvent, handlerCancellation.Token)));
        var cancellationSignal = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        while (!handlingTask.IsCompleted)
        {
            var heartbeatDelay = Task.Delay(
                TimeSpan.FromSeconds(settings.HeartbeatIntervalSeconds),
                CancellationToken.None);
            var completedTask = await Task.WhenAny(
                handlingTask,
                heartbeatDelay,
                cancellationSignal);
            if (completedTask == handlingTask)
            {
                break;
            }

            if (completedTask == cancellationSignal)
            {
                handlerCancellation.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }

            var now = timeProvider.GetUtcNow();
            if (!await RenewAsync(
                    integrationEvent.EventId,
                    lockId,
                    now,
                    now.AddSeconds(settings.LockDurationSeconds),
                    cancellationToken))
            {
                handlerCancellation.Cancel();
                throw new InvalidOperationException("The outbox processing lock expired.");
            }
        }

        await handlingTask;
    }

    private async Task<bool> RenewAsync(
        Guid messageId,
        Guid lockId,
        DateTimeOffset now,
        DateTimeOffset lockedUntil,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IOutboxStore>().RenewAsync(
            messageId,
            lockId,
            now,
            lockedUntil,
            cancellationToken);
    }

    private async Task<bool> CompleteAsync(
        Guid messageId,
        Guid lockId,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IOutboxStore>().CompleteAsync(
            messageId,
            lockId,
            timeProvider.GetUtcNow(),
            cancellationToken);
    }

    private async Task FailAsync(
        Guid messageId,
        Guid lockId,
        string failure,
        DateTimeOffset failedAt,
        DateTimeOffset? nextRetryAt,
        bool deadLetter,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IOutboxStore>().FailAsync(
            messageId,
            lockId,
            failure,
            failedAt,
            nextRetryAt,
            deadLetter,
            cancellationToken);
    }

    private async Task ReleaseAsync(Guid messageId, Guid lockId)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IOutboxStore>().ReleaseAsync(
            messageId,
            lockId,
            timeProvider.GetUtcNow(),
            CancellationToken.None);
    }

    private static TimeSpan CalculateBackoff(int retryCount, OutboxOptions settings)
    {
        var exponent = Math.Min(retryCount, 30);
        var seconds = Math.Min(
            settings.MaximumRetryDelaySeconds,
            settings.RetryBaseDelaySeconds * Math.Pow(2, exponent));
        return TimeSpan.FromSeconds(seconds);
    }

    private static string SanitizeFailure(Exception exception, bool permanent) =>
        permanent
            ? exception.Message[..Math.Min(exception.Message.Length, 4000)]
            : $"Transient delivery failure ({exception.GetType().Name}).";

    [LoggerMessage(
        EventId = 6101,
        Level = LogLevel.Warning,
        Message = "Outbox message {MessageId} failed with {FailureType}; dead letter: {DeadLetter}")]
    private static partial void DispatchFailed(
        ILogger logger,
        Guid messageId,
        string failureType,
        bool deadLetter);
}

public sealed partial class OutboxDispatchHostedService(
    IOutboxDispatcher dispatcher,
    IOptions<OutboxOptions> options,
    ILogger<OutboxDispatchHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await dispatcher.DispatchBatchAsync(stoppingToken);
                if (result.ClaimedCount > 0)
                {
                    BatchCompleted(logger, result.ClaimedCount, result.ProcessedCount, result.FailedCount);
                }

                if (result.ClaimedCount == 0)
                {
                    await Task.Delay(
                        TimeSpan.FromMilliseconds(options.Value.PollIntervalMilliseconds),
                        stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                LoopFailed(logger, exception.GetType().Name);
                try
                {
                    await Task.Delay(
                        TimeSpan.FromMilliseconds(options.Value.PollIntervalMilliseconds),
                        stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    [LoggerMessage(
        EventId = 6100,
        Level = LogLevel.Information,
        Message = "Outbox batch claimed {ClaimedCount}, processed {ProcessedCount}, and failed {FailedCount} messages")]
    private static partial void BatchCompleted(
        ILogger logger,
        int claimedCount,
        int processedCount,
        int failedCount);

    [LoggerMessage(
        EventId = 6102,
        Level = LogLevel.Warning,
        Message = "Outbox dispatch loop failed with {FailureType}")]
    private static partial void LoopFailed(ILogger logger, string failureType);
}

internal sealed class OutboxAdministrationService(
    GoldInvoiceDbContext dbContext,
    TimeProvider timeProvider) : IOutboxAdministrationService
{
    private const int MaximumPageSize = 100;

    public async Task<PagedResult<DeadLetterInfo>> GetDeadLettersAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        ValidatePage(page, pageSize);
        var query = dbContext.OutboxMessages
            .AsNoTracking()
            .Where(message => message.Status == OutboxMessageStatus.DeadLetter);
        var count = await query.CountAsync(cancellationToken);
        var messages = await query
            .OrderByDescending(message => message.UpdatedAt)
            .ThenBy(message => message.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<DeadLetterInfo>(
            messages.Select(Map).ToArray(),
            page,
            pageSize,
            count);
    }

    public async Task<DeadLetterInfo> ReprocessAsync(
        Guid messageId,
        ReprocessDeadLetterCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (messageId == Guid.Empty || command.ActorUserId == Guid.Empty ||
            string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Trim().Length > 1000)
        {
            throw new ArgumentException("A valid message, actor, and reason are required.", nameof(command));
        }

        await using var transaction = await PersistenceUtilities.BeginSerializableTransactionAsync(
            dbContext,
            cancellationToken);
        var message = await dbContext.OutboxMessages.FindAsync([messageId], cancellationToken) ??
            throw new ApplicationResourceNotFoundException();
        PersistenceUtilities.SetOriginalRowVersion(dbContext, message, command.RowVersion);
        var now = timeProvider.GetUtcNow();
        var oldStatus = message.Status;
        message.Reprocess(now);
        var audit = new AuditLog("Outbox.Reprocessed", "OutboxMessage", message.Id.ToString("D"), now);
        audit.SetContext(command.ActorUserId, command.CorrelationId);
        audit.SetValues(
            JsonSerializer.Serialize(new { Status = oldStatus.ToString(), message.RetryCount }),
            JsonSerializer.Serialize(new
            {
                Status = message.Status.ToString(),
                Reason = command.Reason.Trim(),
                RequestedAt = now
            }));
        dbContext.AuditLogs.Add(audit);
        await PersistenceUtilities.SaveChangesAsync(dbContext, cancellationToken);
        await PersistenceUtilities.CommitAsync(transaction, cancellationToken);
        return Map(message);
    }

    private static DeadLetterInfo Map(OutboxMessage message) => new(
        message.Id,
        message.MessageType,
        message.Status.ToString(),
        message.OccurredAt,
        message.RetryCount,
        message.NextRetryAt,
        message.LastError,
        Convert.ToBase64String(message.RowVersion));

    private static void ValidatePage(int page, int pageSize)
    {
        if (page < 1 || pageSize is < 1 or > MaximumPageSize ||
            ((long)page - 1) * pageSize > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(page));
        }
    }
}

internal sealed class IntegrationEventQueryService(GoldInvoiceDbContext dbContext)
    : IIntegrationEventQueryService
{
    private const int MaximumPageSize = 100;
    private const int CandidateMultiplier = 10;

    public async Task<IntegrationEventPage> GetEventsAsync(
        Guid actorUserId,
        IReadOnlyCollection<string> actorRoles,
        Guid? deviceId,
        DateTimeOffset? afterOccurredAt,
        Guid? afterEventId,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (actorUserId == Guid.Empty || pageSize is < 1 or > MaximumPageSize ||
            (afterOccurredAt is null) != (afterEventId is null))
        {
            throw new ArgumentException("The recovery cursor or page size is invalid.");
        }

        if (deviceId is not null)
        {
            var ownsDevice = await dbContext.DesktopDevices.AsNoTracking().AnyAsync(
                device => device.Id == deviceId &&
                    device.RegisteredByUserId == actorUserId &&
                    device.IsActive,
                cancellationToken);
            if (!ownsDevice)
            {
                throw new ApplicationResourceNotFoundException();
            }
        }

        var query = dbContext.OutboxMessages
            .AsNoTracking()
            .Where(message => message.Status == OutboxMessageStatus.Processed);
        if (afterOccurredAt is not null)
        {
            query = query.Where(message => message.OccurredAt >= afterOccurredAt.Value);
        }

        var candidates = await query
            .OrderBy(message => message.OccurredAt)
            .ThenBy(message => message.Id)
            .Take(pageSize * CandidateMultiplier)
            .Select(message => new
            {
                message.Id,
                message.MessageType,
                message.Payload,
                message.OccurredAt
            })
            .ToListAsync(cancellationToken);
        var roles = actorRoles
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var visible = new List<RecoverableIntegrationEvent>(pageSize);
        DateTimeOffset? nextOccurredAt = null;
        Guid? nextEventId = null;
        foreach (var candidate in candidates)
        {
            if (afterOccurredAt is not null && candidate.OccurredAt == afterOccurredAt &&
                candidate.Id.CompareTo(afterEventId!.Value) <= 0)
            {
                continue;
            }

            nextOccurredAt = candidate.OccurredAt;
            nextEventId = candidate.Id;

            ClaimedIntegrationEvent parsed;
            try
            {
                parsed = IntegrationEventSerializer.Deserialize(
                    candidate.Id,
                    candidate.MessageType,
                    candidate.OccurredAt,
                    candidate.Payload);
            }
            catch (PermanentIntegrationEventException)
            {
                continue;
            }

            var audience = parsed.Envelope.Audience;
            if (!audience.UserIds.Contains(actorUserId) &&
                !audience.Roles.Any(roles.Contains) &&
                (deviceId is null || !audience.DeviceIds.Contains(deviceId.Value)))
            {
                continue;
            }

            visible.Add(new RecoverableIntegrationEvent(
                parsed.EventId,
                parsed.EventType,
                parsed.OccurredAt,
                parsed.Envelope.AggregateType,
                parsed.Envelope.AggregateId,
                parsed.Envelope.Data));
            if (visible.Count == pageSize)
            {
                break;
            }
        }

        return new IntegrationEventPage(
            visible,
            nextOccurredAt,
            nextEventId);
    }
}
