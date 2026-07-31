namespace GoldInvoice.Worker;

public sealed partial class Worker(ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        WorkerStarted(logger);
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown.
        }

        WorkerStopped(logger);
    }

    [LoggerMessage(EventId = 1000, Level = LogLevel.Information, Message = "Background worker started")]
    private static partial void WorkerStarted(ILogger logger);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "Background worker stopped")]
    private static partial void WorkerStopped(ILogger logger);
}
