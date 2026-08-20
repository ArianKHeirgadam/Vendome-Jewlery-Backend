using GoldInvoice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GoldInvoice.Infrastructure.LocalRuntime;

/// <summary>
/// Applies pending migrations on startup when the local-runtime mode is
/// enabled. Customer installations never run the dotnet-ef tool, so the
/// first API start after the installer is the migration moment. The
/// operation is idempotent: a second host (the Worker) starting at the same
/// time either waits for the migration lock or finds nothing to apply.
/// </summary>
public sealed partial class LocalDatabaseInitializer(
    IServiceScopeFactory scopeFactory,
    IOptions<LocalRuntimeOptions> options,
    ILogger<LocalDatabaseInitializer> logger) : IHostedService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.ApplyMigrationsOnStartup)
        {
            MigrationsDisabled(logger);
            return;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        Exception? lastFailure = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var dbContext = scope.ServiceProvider
                    .GetRequiredService<GoldInvoiceDbContext>();
                if (!dbContext.Database.IsSqlServer())
                {
                    SqlServerOnly(logger);
                    return;
                }

                await dbContext.Database.MigrateAsync(cancellationToken);
                MigrationsApplied(logger);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                lastFailure = exception;
                if (attempt < 3)
                {
                    MigrationsRetrying(logger, attempt, exception.GetType().Name);
                    await Task.Delay(RetryDelay, cancellationToken);
                }
            }
        }

        MigrationsFailed(logger, lastFailure!.GetType().Name);
        throw lastFailure;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(
        EventId = 4301,
        Level = LogLevel.Information,
        Message = "Local-runtime migrations are disabled; skipping automatic application")]
    private static partial void MigrationsDisabled(ILogger logger);

    [LoggerMessage(
        EventId = 4302,
        Level = LogLevel.Information,
        Message = "Pending database migrations were applied on startup")]
    private static partial void MigrationsApplied(ILogger logger);

    [LoggerMessage(
        EventId = 4303,
        Level = LogLevel.Warning,
        Message = "Migration attempt {Attempt} failed with {FailureType}; retrying")]
    private static partial void MigrationsRetrying(ILogger logger, int attempt, string failureType);

    [LoggerMessage(
        EventId = 4304,
        Level = LogLevel.Error,
        Message = "Startup migrations exhausted all attempts with {FailureType}")]
    private static partial void MigrationsFailed(ILogger logger, string failureType);

    [LoggerMessage(
        EventId = 4305,
        Level = LogLevel.Warning,
        Message = "Local-runtime migrations require SQL Server; skipping")]
    private static partial void SqlServerOnly(ILogger logger);
}