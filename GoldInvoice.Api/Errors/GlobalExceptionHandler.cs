using GoldInvoice.Application.Common;
using GoldInvoice.Application.Security;
using GoldInvoice.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace GoldInvoice.Api.Errors;

public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            logger.LogDebug(
                "Request {Method} {Path} was cancelled by the client",
                httpContext.Request.Method,
                httpContext.Request.Path);
            return true;
        }

        var statusCode = exception switch
        {
            AuthenticationRejectedException => StatusCodes.Status401Unauthorized,
            SecurityAccessDeniedException => StatusCodes.Status403Forbidden,
            SecurityResourceNotFoundException => StatusCodes.Status404NotFound,
            ApplicationResourceNotFoundException => StatusCodes.Status404NotFound,
            StoreProfileNotConfiguredException => StatusCodes.Status422UnprocessableEntity,
            ApplicationConflictException => StatusCodes.Status409Conflict,
            DomainConflictException => StatusCodes.Status409Conflict,
            BadHttpRequestException badRequestException => badRequestException.StatusCode,
            ArgumentException => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status500InternalServerError
        };

        if (exception is AuthenticationRejectedException or
            SecurityAccessDeniedException or
            SecurityResourceNotFoundException)
        {
            logger.LogInformation(
                "Request {Method} {Path} was rejected with status code {StatusCode}",
                httpContext.Request.Method,
                httpContext.Request.Path,
                statusCode);
        }
        else
        {
            logger.Log(
                statusCode >= StatusCodes.Status500InternalServerError ? LogLevel.Error : LogLevel.Warning,
                exception,
                "Request {Method} {Path} failed with status code {StatusCode}",
                httpContext.Request.Method,
                httpContext.Request.Path,
                statusCode);
        }

        httpContext.Response.StatusCode = statusCode;

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = statusCode switch
            {
                StatusCodes.Status401Unauthorized => "Authentication failed.",
                StatusCodes.Status403Forbidden => "Access denied.",
                StatusCodes.Status404NotFound => "Resource not found.",
                StatusCodes.Status409Conflict => "The operation conflicts with current state.",
                StatusCodes.Status422UnprocessableEntity => "A required setup step is incomplete.",
                >= StatusCodes.Status500InternalServerError => "An unexpected error occurred.",
                _ => "The request could not be processed."
            },
            Detail = statusCode switch
            {
                StatusCodes.Status401Unauthorized => "The credentials or session are invalid.",
                StatusCodes.Status403Forbidden => "This operation is not permitted.",
                StatusCodes.Status404NotFound => "The requested resource does not exist.",
                StatusCodes.Status409Conflict => "Refresh the resource and retry the operation.",
                StatusCodes.Status422UnprocessableEntity =>
                    "Configure the store profile in Settings before creating an order.",
                >= StatusCodes.Status500InternalServerError => "The server could not complete the request.",
                _ => "The request was invalid."
            }
        };

        await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problemDetails
        });

        return true;
    }
}
