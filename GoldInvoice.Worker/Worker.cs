using GoldInvoice.Application.Inventory;
using GoldInvoice.Application.Pricing;
using GoldInvoice.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace GoldInvoice.Worker;

public sealed class WorkerScheduleOptions
{
    public const string SectionName = "Worker";

    public int ReservationSweepIntervalSeconds { get; set; } = 30;

    public static bool IsValid(WorkerScheduleOptions options) =>
        options.ReservationSweepIntervalSeconds is >= 1 and <= 3600;
}

public sealed partial class MarketPriceWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<MarketPriceOptions> options,
    ILogger<MarketPriceWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Started(logger);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var ingestion = scope.ServiceProvider.GetRequiredService<IMarketPriceIngestionService>();
                var storedCount = await ingestion.PollAllAsync(stoppingToken);
                PollCompleted(logger, storedCount);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                PollFailed(logger, exception.GetType().Name);
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(options.Value.PollIntervalMinutes), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        Stopped(logger);
    }

    [LoggerMessage(EventId = 4102, Level = LogLevel.Information, Message = "Market-price worker started")]
    private static partial void Started(ILogger logger);

    [LoggerMessage(EventId = 4103, Level = LogLevel.Information, Message = "Market-price worker stopped")]
    private static partial void Stopped(ILogger logger);

    [LoggerMessage(
        EventId = 4104,
        Level = LogLevel.Information,
        Message = "Market-price worker stored {SnapshotCount} snapshots")]
    private static partial void PollCompleted(ILogger logger, int snapshotCount);

    [LoggerMessage(
        EventId = 4105,
        Level = LogLevel.Warning,
        Message = "Market-price worker failed with {FailureType}")]
    private static partial void PollFailed(ILogger logger, string failureType);
}

public sealed partial class ReservationExpirationWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<WorkerScheduleOptions> options,
    ILogger<ReservationExpirationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Started(logger);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var inventory = scope.ServiceProvider.GetRequiredService<IInventoryService>();
                var expiredCount = await inventory.ExpireReservationsAsync(stoppingToken);
                if (expiredCount > 0)
                {
                    SweepCompleted(logger, expiredCount);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                SweepFailed(logger, exception.GetType().Name);
            }

            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(options.Value.ReservationSweepIntervalSeconds),
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        Stopped(logger);
    }

    [LoggerMessage(EventId = 4200, Level = LogLevel.Information, Message = "Reservation-expiration worker started")]
    private static partial void Started(ILogger logger);

    [LoggerMessage(EventId = 4201, Level = LogLevel.Information, Message = "Reservation-expiration worker stopped")]
    private static partial void Stopped(ILogger logger);

    [LoggerMessage(
        EventId = 4202,
        Level = LogLevel.Information,
        Message = "Reservation-expiration worker expired {ExpiredReservationCount} reservations")]
    private static partial void SweepCompleted(ILogger logger, int expiredReservationCount);

    [LoggerMessage(
        EventId = 4203,
        Level = LogLevel.Warning,
        Message = "Reservation-expiration worker failed with {FailureType}")]
    private static partial void SweepFailed(ILogger logger, string failureType);
}
