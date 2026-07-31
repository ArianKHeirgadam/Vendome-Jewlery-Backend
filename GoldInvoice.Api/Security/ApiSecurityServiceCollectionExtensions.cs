using System.Threading.RateLimiting;
using GoldInvoice.Api.Configuration;
using GoldInvoice.Application.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace GoldInvoice.Api.Security;

public static class ApiSecurityServiceCollectionExtensions
{
    public static IServiceCollection AddApiSecurity(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = BearerTokenAuthenticationHandler.SchemeName;
                options.DefaultChallengeScheme = BearerTokenAuthenticationHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, BearerTokenAuthenticationHandler>(
                BearerTokenAuthenticationHandler.SchemeName,
                _ => { });

        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();

            foreach (var permission in SecurityPermissions.All)
            {
                options.AddPolicy(permission.Name, policy =>
                    policy.RequireAuthenticatedUser()
                        .RequireClaim(SecurityClaimNames.Permission, permission.Name));
            }
        });

        var section = configuration.GetSection(AuthenticationRateLimitOptions.SectionName);
        var settings = section.Get<AuthenticationRateLimitOptions>() ?? new AuthenticationRateLimitOptions();
        services
            .AddOptions<AuthenticationRateLimitOptions>()
            .Bind(section)
            .Validate(AuthenticationRateLimitOptions.IsValid, "Authentication rate limits are invalid.")
            .ValidateOnStart();
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            AddFixedWindowPolicy(options, RateLimitPolicyNames.Login, settings.Login);
            AddFixedWindowPolicy(options, RateLimitPolicyNames.Refresh, settings.Refresh);
            AddFixedWindowPolicy(options, RateLimitPolicyNames.Mfa, settings.Mfa);
            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                var problemDetailsService = context.HttpContext.RequestServices
                    .GetRequiredService<IProblemDetailsService>();
                await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
                {
                    HttpContext = context.HttpContext,
                    ProblemDetails = new ProblemDetails
                    {
                        Status = StatusCodes.Status429TooManyRequests,
                        Title = "Too many requests.",
                        Detail = "Wait before trying this operation again."
                    }
                });
            };
        });

        return services;
    }

    private static void AddFixedWindowPolicy(
        RateLimiterOptions options,
        string policyName,
        RateLimitRule rule)
    {
        options.AddPolicy(policyName, httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = rule.PermitLimit,
                    Window = TimeSpan.FromSeconds(rule.WindowSeconds),
                    QueueLimit = 0,
                    AutoReplenishment = true
                }));
    }
}
