using GoldInvoice.Application.Inventory;
using GoldInvoice.Application.Pricing;
using GoldInvoice.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace GoldInvoice.Worker;

public sealed partial class Worker(
    IServiceScopeFactory scopeFactory,
    IOptions<MarketPriceOptions> options,
    TimeProvider timeProvider,
    ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        WorkerStarted(logger);
        var nextMarketPollAt = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var ingestion = scope.ServiceProvider.GetRequiredService<IMarketPriceIngestionService>();
                var inventory = scope.ServiceProvider.GetRequiredService<IInventoryService>();
                var expiredCount = await inventory.ExpireReservationsAsync(stoppingToken);
                var now = timeProvider.GetUtcNow();
                var marketPollDue = now >= nextMarketPollAt;
                var storedCount = 0;
                if (marketPollDue)
                {
                    storedCount = await ingestion.PollAllAsync(stoppingToken);
                    nextMarketPollAt = now.AddMinutes(options.Value.PollIntervalMinutes);
                }

                if (marketPollDue || expiredCount > 0)
                {
                    PollCompleted(logger, storedCount, expiredCount);
                }

                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                PollFailed(logger, exception.GetType().Name);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        WorkerStopped(logger);
    }

    [LoggerMessage(EventId = 1000, Level = LogLevel.Information, Message = "Background worker started")]
    private static partial void WorkerStarted(ILogger logger);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "Background worker stopped")]
    private static partial void WorkerStopped(ILogger logger);

    [LoggerMessage(
        EventId = 4102,
        Level = LogLevel.Information,
        Message = "Worker stored {SnapshotCount} market snapshots and expired {ExpiredReservationCount} reservations")]
    private static partial void PollCompleted(
        ILogger logger,
        int snapshotCount,
        int expiredReservationCount);

    [LoggerMessage(
        EventId = 4103,
        Level = LogLevel.Warning,
        Message = "Market-price poll failed with {FailureType}")]
    private static partial void PollFailed(ILogger logger, string failureType);
}
