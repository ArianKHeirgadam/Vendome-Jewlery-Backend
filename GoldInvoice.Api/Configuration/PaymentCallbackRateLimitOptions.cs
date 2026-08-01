namespace GoldInvoice.Api.Configuration;

public sealed class PaymentCallbackRateLimitOptions
{
    public const string SectionName = "RateLimiting:PaymentCallbacks";

    public RateLimitRule Rule { get; init; } = new(60, 60);

    public static bool IsValid(PaymentCallbackRateLimitOptions options) => options.Rule.IsValid();
}
