using GoldInvoice.Api.Configuration;
using GoldInvoice.Api.Errors;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Net.Http.Headers;

namespace GoldInvoice.Api;

public static class ApiServiceCollectionExtensions
{
    public const string CorsPolicyName = "ConfiguredOrigins";

    public static IServiceCollection AddApiFoundation(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var apiSection = configuration.GetSection(ApiHostOptions.SectionName);
        var correlationSection = configuration.GetSection(CorrelationIdOptions.SectionName);

        services
            .AddOptions<ApiHostOptions>()
            .Bind(apiSection)
            .Validate(IsValidApiOptions, "API paths or CORS origins are invalid.")
            .ValidateOnStart();

        services
            .AddOptions<CorrelationIdOptions>()
            .Bind(correlationSection)
            .Validate(IsValidCorrelationOptions, "Correlation ID settings are invalid.")
            .ValidateOnStart();

        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = context =>
            {
                context.ProblemDetails.Instance ??= context.HttpContext.Request.Path;
                context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
            };
        });
        services.AddExceptionHandler<GlobalExceptionHandler>();
        services
            .AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"]);

        AddCors(services, apiSection.Get<ApiHostOptions>() ?? new ApiHostOptions(),
            correlationSection.Get<CorrelationIdOptions>() ?? new CorrelationIdOptions());

        return services;
    }

    private static void AddCors(
        IServiceCollection services,
        ApiHostOptions apiOptions,
        CorrelationIdOptions correlationOptions)
    {
        var configuredOrigins = apiOptions.AllowedCorsOrigins
            ?.Where(origin => !string.IsNullOrWhiteSpace(origin))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];

        services.AddCors(options =>
        {
            options.AddPolicy(CorsPolicyName, policy =>
            {
                if (configuredOrigins.Length > 0)
                {
                    policy.WithOrigins(configuredOrigins);
                }

                policy
                    .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS")
                    .WithHeaders(
                        HeaderNames.Authorization,
                        HeaderNames.ContentType,
                        correlationOptions.HeaderName,
                        "X-SignalR-User-Agent",
                        "Idempotency-Key")
                    .WithExposedHeaders(correlationOptions.HeaderName)
                    .SetPreflightMaxAge(TimeSpan.FromHours(1));
            });
        });
    }

    private static bool IsValidApiOptions(ApiHostOptions options)
    {
        if (!IsValidPath(options.LivenessPath) ||
            !IsValidPath(options.ReadinessPath) ||
            string.Equals(options.LivenessPath, options.ReadinessPath, StringComparison.OrdinalIgnoreCase) ||
            options.AllowedCorsOrigins is null)
        {
            return false;
        }

        return options.AllowedCorsOrigins.All(IsValidCorsOrigin);
    }

    private static bool IsValidPath(string path) =>
        !string.IsNullOrWhiteSpace(path) &&
        path.StartsWith('/') &&
        !path.Contains('?') &&
        !path.Contains('#');

    private static bool IsValidCorsOrigin(string origin)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            uri.AbsolutePath != "/" ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        return uri.Scheme == Uri.UriSchemeHttps ||
            (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback);
    }

    private static bool IsValidCorrelationOptions(CorrelationIdOptions options) =>
        options.MaxLength is >= 16 and <= 128 &&
        !string.IsNullOrWhiteSpace(options.HeaderName) &&
        options.HeaderName.Length <= 64 &&
        options.HeaderName.All(character =>
            char.IsAsciiLetterOrDigit(character) || character == '-');
}
