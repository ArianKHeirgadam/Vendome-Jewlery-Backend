using Microsoft.Extensions.Options;

namespace GoldInvoice.Infrastructure.LocalRuntime;

public interface ILocalDataDirectoryProvider
{
    string DataDirectory { get; }

    string BackupDirectory { get; }
}

internal sealed class LocalDataDirectoryProvider(
    IOptions<LocalRuntimeOptions> options) : ILocalDataDirectoryProvider
{
    public string DataDirectory => LocalRuntimePaths.Resolve(options.Value.DataDirectory);

    public string BackupDirectory => LocalRuntimePaths.Resolve(
        string.IsNullOrWhiteSpace(options.Value.BackupDirectory)
            ? Path.Combine(DataDirectory, "Backups")
            : options.Value.BackupDirectory);
}

internal static class LocalRuntimePaths
{
    public static string Resolve(string configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new ArgumentException("A local-runtime directory is required.", nameof(configured));
        }

        return Path.GetFullPath(Environment.ExpandEnvironmentVariables(configured));
    }

    public static string BackupFileNamePattern(string prefix) => $"{prefix}-*.bak";
}