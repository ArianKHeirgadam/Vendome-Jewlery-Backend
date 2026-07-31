namespace GoldInvoice.Infrastructure.Configuration;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public int CommandTimeoutSeconds { get; init; } = 30;

    public bool EnableDetailedErrors { get; init; }
}
