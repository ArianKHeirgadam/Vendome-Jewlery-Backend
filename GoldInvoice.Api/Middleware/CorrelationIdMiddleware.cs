using System.Diagnostics;
using GoldInvoice.Api.Configuration;
using Microsoft.Extensions.Options;

namespace GoldInvoice.Api.Middleware;

public sealed class CorrelationIdMiddleware(
    RequestDelegate next,
    IOptions<CorrelationIdOptions> options,
    ILogger<CorrelationIdMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ResolveCorrelationId(context.Request, options.Value);

        context.TraceIdentifier = correlationId;
        context.Response.Headers[options.Value.HeaderName] = correlationId;

        using (logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId
        }))
        {
            await next(context);
        }
    }

    private static string ResolveCorrelationId(
        HttpRequest request,
        CorrelationIdOptions correlationOptions)
    {
        var values = request.Headers[correlationOptions.HeaderName];

        if (values.Count == 1)
        {
            var candidate = values[0];
            if (candidate is not null && IsValid(candidate, correlationOptions.MaxLength))
            {
                return candidate;
            }
        }

        return Activity.Current?.TraceId.ToHexString() ?? Guid.NewGuid().ToString("N");
    }

    private static bool IsValid(string value, int maxLength) =>
        value.Length is > 0 &&
        value.Length <= maxLength &&
        value.All(character =>
            char.IsAsciiLetterOrDigit(character) ||
            character is '-' or '_' or '.');
}
