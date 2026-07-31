namespace GoldInvoice.Infrastructure.Configuration;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; init; } = string.Empty;

    public string Audience { get; init; } = string.Empty;

    public string SigningKey { get; init; } = string.Empty;

    public int AccessTokenLifetimeMinutes { get; init; } = 10;

    public int MfaEnrollmentTokenLifetimeMinutes { get; init; } = 10;

    public int ClockSkewSeconds { get; init; } = 30;

    public static bool IsValid(JwtOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Issuer) ||
            string.IsNullOrWhiteSpace(options.Audience) ||
            options.AccessTokenLifetimeMinutes is < 1 or > 30 ||
            options.MfaEnrollmentTokenLifetimeMinutes is < 2 or > 15 ||
            options.ClockSkewSeconds is < 0 or > 120)
        {
            return false;
        }

        try
        {
            return Convert.FromBase64String(options.SigningKey).Length >= 32;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
