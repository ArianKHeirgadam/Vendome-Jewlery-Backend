namespace GoldInvoice.Infrastructure.Configuration;

public sealed class IdentitySecurityOptions
{
    public const string SectionName = "Security";

    public int PasswordRequiredLength { get; init; } = 12;

    public int MaxFailedAccessAttempts { get; init; } = 5;

    public int LockoutMinutes { get; init; } = 15;

    public int SessionLifetimeDays { get; init; } = 30;

    public int RefreshTokenLifetimeDays { get; init; } = 14;

    public int RecoveryCodeCount { get; init; } = 10;

    public string AuthenticatorIssuer { get; init; } = "Vendome Jewelry";

    public static bool IsValid(IdentitySecurityOptions options) =>
        options.PasswordRequiredLength is >= 12 and <= 128 &&
        options.MaxFailedAccessAttempts is >= 3 and <= 10 &&
        options.LockoutMinutes is >= 5 and <= 1440 &&
        options.SessionLifetimeDays is >= 1 and <= 90 &&
        options.RefreshTokenLifetimeDays is >= 1 and <= 90 &&
        options.RefreshTokenLifetimeDays <= options.SessionLifetimeDays &&
        options.RecoveryCodeCount is >= 5 and <= 20 &&
        !string.IsNullOrWhiteSpace(options.AuthenticatorIssuer) &&
        options.AuthenticatorIssuer.Length <= 100;
}
