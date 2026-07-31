namespace GoldInvoice.Api.Configuration;

public sealed class ApiHostOptions
{
    public const string SectionName = "Api";

    public string LivenessPath { get; set; } = "/health/live";

    public string ReadinessPath { get; set; } = "/health/ready";

    public string[] AllowedCorsOrigins { get; set; } = [];
}
