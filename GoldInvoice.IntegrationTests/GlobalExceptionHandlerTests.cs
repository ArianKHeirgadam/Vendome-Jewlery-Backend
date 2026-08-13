using GoldInvoice.Application.Security;
using GoldInvoice.Application.Common;
using GoldInvoice.Api.Errors;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace GoldInvoice.IntegrationTests;

public sealed class GlobalExceptionHandlerTests
{
    [Fact]
    public async Task TryHandleAsync_ForUnhandledException_ReturnsSanitizedProblemDetails()
    {
        const string sensitiveMessage = "sensitive internal detail";
        var problemDetailsService = new RecordingProblemDetailsService();
        var handler = new GlobalExceptionHandler(
            problemDetailsService,
            NullLogger<GlobalExceptionHandler>.Instance);
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/invoices";

        var handled = await handler.TryHandleAsync(
            context,
            new InvalidOperationException(sensitiveMessage),
            CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Equal(
            StatusCodes.Status500InternalServerError,
            problemDetailsService.Context?.ProblemDetails.Status);
        Assert.DoesNotContain(
            sensitiveMessage,
            problemDetailsService.Context?.ProblemDetails.Detail ?? string.Empty,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task TryHandleAsync_ForBadArgument_ReturnsBadRequestProblemDetails()
    {
        var problemDetailsService = new RecordingProblemDetailsService();
        var handler = new GlobalExceptionHandler(
            problemDetailsService,
            NullLogger<GlobalExceptionHandler>.Instance);
        var context = new DefaultHttpContext();

        var handled = await handler.TryHandleAsync(
            context,
            new ArgumentException("Invalid input"),
            CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Equal(
            StatusCodes.Status400BadRequest,
            problemDetailsService.Context?.ProblemDetails.Status);
    }

    [Fact]
    public async Task TryHandleAsync_ForRejectedAuthentication_ReturnsGenericUnauthorizedProblemDetails()
    {
        var problemDetailsService = new RecordingProblemDetailsService();
        var handler = new GlobalExceptionHandler(
            problemDetailsService,
            NullLogger<GlobalExceptionHandler>.Instance);
        var context = new DefaultHttpContext();

        var handled = await handler.TryHandleAsync(
            context,
            new AuthenticationRejectedException(),
            CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.Equal("Authentication failed.", problemDetailsService.Context?.ProblemDetails.Title);
        Assert.DoesNotContain(
            "password",
            problemDetailsService.Context?.ProblemDetails.Detail ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TryHandleAsync_ForMissingStoreProfile_ReturnsSetupPrecondition()
    {
        var problemDetailsService = new RecordingProblemDetailsService();
        var handler = new GlobalExceptionHandler(
            problemDetailsService,
            NullLogger<GlobalExceptionHandler>.Instance);
        var context = new DefaultHttpContext();

        var handled = await handler.TryHandleAsync(
            context,
            new StoreProfileNotConfiguredException(),
            CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, context.Response.StatusCode);
        Assert.Contains(
            "store profile",
            problemDetailsService.Context?.ProblemDetails.Detail ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
    }

    private sealed class RecordingProblemDetailsService : IProblemDetailsService
    {
        public ProblemDetailsContext? Context { get; private set; }

        public ValueTask WriteAsync(ProblemDetailsContext context)
        {
            Context = context;
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> TryWriteAsync(ProblemDetailsContext context)
        {
            Context = context;
            return ValueTask.FromResult(true);
        }
    }
}
