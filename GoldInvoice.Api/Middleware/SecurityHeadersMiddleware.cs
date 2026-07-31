namespace GoldInvoice.Api.Middleware;

public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            context.Response.Headers.XContentTypeOptions = "nosniff";
            context.Response.Headers.XFrameOptions = "DENY";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
            }

            if (context.Request.Path.StartsWithSegments("/api/v1/auth"))
            {
                context.Response.Headers.CacheControl = "no-store";
                context.Response.Headers.Pragma = "no-cache";
            }

            return Task.CompletedTask;
        });

        await next(context);
    }
}
