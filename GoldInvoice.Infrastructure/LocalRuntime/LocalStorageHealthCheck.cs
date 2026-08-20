using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace GoldInvoice.Infrastructure.LocalRuntime;

/// <summary>
/// Verifies that the local data and backup folders exist and are writable.
/// Only registered when local-runtime mode is active, so network and
/// development deployments are not affected by it.
/// </summary>
public sealed partial class LocalStorageHealthCheck(
    ILocalDataDirectoryProvider directories,
    ILogger<LocalStorageHealthCheck> logger) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var failures = new List<string>(capacity: 2);

        Probe(directories.DataDirectory, "data", failures);
        Probe(directories.BackupDirectory, "backup", failures);

        return failures.Count == 0
            ? Task.FromResult(HealthCheckResult.Healthy("Local data and backup folders are writable."))
            : Task.FromResult(HealthCheckResult.Unhealthy(
                "Local storage is not available: " + string.Join("; ", failures)));
    }

    private void Probe(string directory, string label, List<string> failures)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var probe = Path.Combine(directory, $".probe-{Environment.ProcessId}-{Guid.NewGuid():N}");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
        }
        catch (Exception exception)
        {
            failures.Add($"{label} folder '{directory}' is not writable ({exception.GetType().Name})");
            StorageUnavailable(logger, label, exception.GetType().Name);
        }
    }

    [LoggerMessage(
        EventId = 4320,
        Level = LogLevel.Warning,
        Message = "Local {Label} storage probe failed with {FailureType}")]
    private static partial void StorageUnavailable(ILogger logger, string label, string failureType);
}