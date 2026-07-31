namespace GoldInvoice.Api.Configuration;

public sealed class AuthenticationRateLimitOptions
{
    public const string SectionName = "RateLimiting:Authentication";

    public RateLimitRule Login { get; init; } = new(5, 60);

    public RateLimitRule Refresh { get; init; } = new(10, 60);

    public RateLimitRule Mfa { get; init; } = new(5, 60);

    public static bool IsValid(AuthenticationRateLimitOptions options) =>
        options.Login.IsValid() && options.Refresh.IsValid() && options.Mfa.IsValid();
}

public sealed record RateLimitRule(int PermitLimit, int WindowSeconds)
{
    public bool IsValid() =>
        PermitLimit is >= 1 and <= 100 && WindowSeconds is >= 10 and <= 3600;
}
