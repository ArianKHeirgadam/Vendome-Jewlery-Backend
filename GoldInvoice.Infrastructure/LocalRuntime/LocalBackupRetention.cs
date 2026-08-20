namespace GoldInvoice.Infrastructure.LocalRuntime;

/// <summary>
/// Pure filesystem policy: deletes the oldest backup files beyond the
/// configured retention count and returns the deleted paths. Kept
/// intentionally free of IO dependencies so every branch is unit-testable.
/// </summary>
internal static class LocalBackupRetention
{
    public const string BackupFilePattern = "vendome-*.bak";

    public static IReadOnlyList<string> Prune(string directory, int keep)
    {
        if (keep < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(keep));
        }

        if (!Directory.Exists(directory))
        {
            return [];
        }

        var candidates = Directory.GetFiles(directory, BackupFilePattern)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToArray();
        var remove = candidates.Skip(keep).ToArray();
        foreach (var file in remove)
        {
            File.Delete(file);
        }

        return remove;
    }
}