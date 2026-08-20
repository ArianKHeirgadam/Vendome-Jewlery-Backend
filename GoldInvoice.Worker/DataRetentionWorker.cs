using GoldInvoice.Infrastructure.Configuration;
using GoldInvoice.Infrastructure.Integration;
using Microsoft.Extensions.Options;

namespace GoldInvoice.Worker;

public sealed partial class DataRetentionWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<RetentionOptions> options,
    ILogger<DataRetentionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Started(logger);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var retention = scope.ServiceProvider.GetRequiredService<IDataRetentionService>();
                var result = await retention.SweepAsync(stoppingToken);
                if (result.PurgedOutboxMessages > 0 ||
                    result.PurgedIdempotencyRecords > 0 ||
                    result.PurgedPaymentCallbacks > 0)
                {
                    SweepCompleted(logger, result);
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
                    TimeSpan.FromHours(options.Value.SweepIntervalHours),
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        Stopped(logger);
    }

    [LoggerMessage(EventId = 4300, Level = LogLevel.Information, Message = "Data-retention worker started")]
    private static partial void Started(ILogger logger);

    [LoggerMessage(EventId = 4301, Level = LogLevel.Information, Message = "Data-retention worker stopped")]
    private static partial void Stopped(ILogger logger);

    [LoggerMessage(EventId = 4302, Level = LogLevel.Information, Message = "Data-retention sweep removed {Result}")]
    private static partial void SweepCompleted(ILogger logger, DataRetentionResult result);

    [LoggerMessage(EventId = 4303, Level = LogLevel.Warning, Message = "Data-retention worker failed with {FailureType}")]
    private static partial void SweepFailed(ILogger logger, string failureType);
}