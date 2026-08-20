using System.Text.RegularExpressions;
using GoldInvoice.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GoldInvoice.Infrastructure.LocalRuntime;

/// <summary>
/// Scheduled SQL Server backup worker for local installations. Runs an
/// immediate backup at startup (validating the backup pipeline) and then
/// backs up on the configured interval. Files are kept in the backup
/// directory and pruned by <see cref="LocalBackupRetention"/>. This worker
/// is only meaningful for SQL Server; other providers are skipped.
/// </summary>
public sealed partial class LocalBackupWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<LocalRuntimeOptions> options,
    ILocalDataDirectoryProvider directories,
    TimeProvider timeProvider,
    ILogger<LocalBackupWorker> logger) : BackgroundService
{
    private const string BackupFilePrefix = "vendome";

    private static readonly Regex SafeDatabaseName = new(
        "^[A-Za-z0-9_]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.BackupEnabled)
        {
            BackupsDisabled(logger);
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var result = await BackupOnceAsync(scope.ServiceProvider, stoppingToken);
                if (result is not null)
                {
                    BackupCompleted(logger, result.Path, result.BackupCount, result.PrunedCount);
                }
                else
                {
                    NotApplicable(logger);
                    return;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                BackupFailed(logger, exception.GetType().Name);
            }

            try
            {
                await Task.Delay(
                    TimeSpan.FromHours(options.Value.BackupIntervalHours),
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task<BackupOutcome?> BackupOnceAsync(
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        var dbContext = services.GetRequiredService<GoldInvoiceDbContext>();
        if (!dbContext.Database.IsSqlServer())
        {
            return null;
        }

        var databaseName = dbContext.Database.GetDbConnection().Database;
        if (string.IsNullOrWhiteSpace(databaseName) ||
            !SafeDatabaseName.IsMatch(databaseName))
        {
            throw new InvalidOperationException(
                "The backup database name could not be validated.");
        }

        var stamp = timeProvider.GetUtcNow().ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var backupDirectory = directories.BackupDirectory;
        Directory.CreateDirectory(backupDirectory);
        var backupPath = Path.Combine(backupDirectory, $"{BackupFilePrefix}-{stamp}.bak");

        await dbContext.Database.ExecuteSqlRawAsync(
            $"BACKUP DATABASE [{databaseName}] TO DISK = @path WITH INIT, COMPRESSION",
            [new SqlParameter("path", backupPath)],
            cancellationToken);

        var pruned = LocalBackupRetention.Prune(
            backupDirectory,
            options.Value.BackupsToKeep);
        var backupCount = Directory.GetFiles(backupDirectory, LocalBackupRetention.BackupFilePattern).Length;
        return new BackupOutcome(backupPath, backupCount, pruned.Count);
    }

    private sealed record BackupOutcome(string Path, int BackupCount, int PrunedCount);

    [LoggerMessage(
        EventId = 4310,
        Level = LogLevel.Information,
        Message = "Local-runtime backups are disabled; skipping")]
    private static partial void BackupsDisabled(ILogger logger);

    [LoggerMessage(
        EventId = 4311,
        Level = LogLevel.Information,
        Message = "Database backup written to {BackupPath}; {BackupCount} backups kept, {PrunedCount} pruned")]
    private static partial void BackupCompleted(ILogger logger, string backupPath, int backupCount, int prunedCount);

    [LoggerMessage(
        EventId = 4312,
        Level = LogLevel.Warning,
        Message = "Local-runtime backups require SQL Server; skipping")]
    private static partial void NotApplicable(ILogger logger);

    [LoggerMessage(
        EventId = 4313,
        Level = LogLevel.Warning,
        Message = "Database backup failed with {FailureType}")]
    private static partial void BackupFailed(ILogger logger, string failureType);
}