namespace GoldInvoice.Infrastructure.LocalRuntime;

/// <summary>
/// Settings for invisible per-machine installations (single desktop for a
/// stores). These values are consumed by the local database initializer,
/// the scheduled backup worker, and the storage health check. Every feature
/// is off by default so network/development deployments behave exactly as
/// before.
/// </summary>
public sealed class LocalRuntimeOptions
{
    public const string SectionName = "LocalRuntime";

    /// <summary>Root folder for the machine-local data files. Supports Windows
    /// environment variables such as %ProgramData%.</summary>
    public string DataDirectory { get; set; } = "%ProgramData%\\Vendome";

    /// <summary>Backup folder; when empty it defaults to
    /// <c>&lt;DataDirectory&gt;\Backups</c>.</summary>
    public string BackupDirectory { get; set; } = "";

    /// <summary>Applies pending EF Core migrations automatically when the
    /// API or Worker starts. Required for customer machines where the
    /// dotnet-ef tool is never available.</summary>
    public bool ApplyMigrationsOnStartup { get; set; }

    public bool BackupEnabled { get; set; }

    public double BackupIntervalHours { get; set; } = 24;

    public int BackupsToKeep { get; set; } = 14;

    public static bool IsValid(LocalRuntimeOptions options) =>
        !string.IsNullOrWhiteSpace(options.DataDirectory) &&
        options.BackupIntervalHours is >= 1 and <= 168 &&
        options.BackupsToKeep is >= 1 and <= 365;
}