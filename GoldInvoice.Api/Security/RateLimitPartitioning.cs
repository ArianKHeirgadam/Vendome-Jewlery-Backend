namespace GoldInvoice.Api.Security;

/// <summary>
/// Resolves the rate-limiting partition key for a request. The key must be
/// stable per client. IPv4-mapped IPv6 addresses (for example
/// "::ffff:127.0.0.1") are normalized to their IPv4 form so a client does
/// not split its budget across two buckets depending on Kestrel's view of
/// the socket address. When forwarded headers are enabled through
/// UseForwardedHeaders, <see cref="HttpContext.Connection.RemoteIpAddress"/>
/// already reflects the caller provided by the trusted proxy.
/// </summary>
public static class RateLimitPartitioning
{
    public static string ResolveKey(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var address = httpContext.Connection.RemoteIpAddress;
        if (address is null)
        {
            // Requests without a remote address share one bucket so they
            // cannot bypass the limit via an empty partition key.
            return "unknown";
        }

        return address.IsIPv4MappedToIPv6
            ? address.MapToIPv4().ToString()
            : address.ToString();
    }
}