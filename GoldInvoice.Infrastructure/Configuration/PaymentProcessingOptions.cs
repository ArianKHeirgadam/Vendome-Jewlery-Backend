namespace GoldInvoice.Infrastructure.Configuration;

public sealed class PaymentProcessingOptions
{
    public const string SectionName = "Payments";

    public int ProviderTimeoutSeconds { get; init; } = 15;

    public int MaximumGatewayConfigurationsPerProvider { get; init; } = 5;

    public int ReservationReactivationMinutes { get; init; } = 30;

    public static bool IsValid(PaymentProcessingOptions options) =>
        options.ProviderTimeoutSeconds is >= 1 and <= 60 &&
        options.MaximumGatewayConfigurationsPerProvider is >= 1 and <= 20 &&
        options.ReservationReactivationMinutes is >= 1 and <= 1440;
}
