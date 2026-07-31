namespace GoldInvoice.Api.Configuration;

public sealed class CorrelationIdOptions
{
    public const string SectionName = "CorrelationId";

    public string HeaderName { get; set; } = "X-Correlation-ID";

    public int MaxLength { get; set; } = 64;
}
