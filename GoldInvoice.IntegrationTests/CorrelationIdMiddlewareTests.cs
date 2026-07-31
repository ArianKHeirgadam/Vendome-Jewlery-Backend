using GoldInvoice.Api.Configuration;
using GoldInvoice.Api.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GoldInvoice.IntegrationTests;

public sealed class CorrelationIdMiddlewareTests
{
    private static readonly CorrelationIdOptions CorrelationOptions = new();

    [Fact]
    public async Task InvokeAsync_WithValidRequestId_PreservesItAcrossTheRequest()
    {
        const string requestId = "checkout-request-123";
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationOptions.HeaderName] = requestId;
        var middleware = CreateMiddleware();

        await middleware.InvokeAsync(context);

        Assert.Equal(requestId, context.TraceIdentifier);
        Assert.Equal(requestId, context.Response.Headers[CorrelationOptions.HeaderName]);
    }

    [Fact]
    public async Task InvokeAsync_WithUnsafeRequestId_GeneratesASafeReplacement()
    {
        const string unsafeRequestId = "unsafe/value";
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationOptions.HeaderName] = unsafeRequestId;
        var middleware = CreateMiddleware();

        await middleware.InvokeAsync(context);

        Assert.NotEqual(unsafeRequestId, context.TraceIdentifier);
        Assert.InRange(context.TraceIdentifier.Length, 1, CorrelationOptions.MaxLength);
        Assert.All(context.TraceIdentifier, character =>
            Assert.True(char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'));
        Assert.Equal(
            context.TraceIdentifier,
            context.Response.Headers[CorrelationOptions.HeaderName]);
    }

    private static CorrelationIdMiddleware CreateMiddleware() =>
        new(
            _ => Task.CompletedTask,
            Options.Create(CorrelationOptions),
            NullLogger<CorrelationIdMiddleware>.Instance);
}
