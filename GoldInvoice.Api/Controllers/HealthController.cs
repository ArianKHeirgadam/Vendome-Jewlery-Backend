using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics;
using System;

namespace GoldInvoice.Api.Controllers

[ApiController]
[Route("health")]
public class HealthController : ControllerBase
{
    private readonly IDiagnosticListener _diagnosticListener;

    public HealthController(IDiagnosticListener diagnosticListener)
    {
        _diagnosticListener = diagnosticListener;
    }

    [HttpGet("live")]
    public IActionResult GetLive()
    {
        var correlationId = HttpContext.TraceIdentifier;
        Response.Headers["X-Correlation-ID"] = correlationId;

        return Ok(new
        {
            status = "healthy",
            timestamp = DateTimeOffset.UtcNow,
            correlationId = correlationId
        });
    }

    [HttpGet("ready")]
    public IActionResult GetReady()
    {
        var correlationId = HttpContext.TraceIdentifier;
        Response.Headers["X-Correlation-ID"] = correlationId;

        var healthSnapshot = _diagnosticListener.GetCurrentSnapshot();
        var isReady = healthSnapshot?.GetHealthStatus() == Microsoft.Extensions.Diagnostics.HealthCheck.HealthStatus.Healthy;

        if (isReady)
        {
            return Ok(new
            {
                status = "ready",
                timestamp = DateTimeOffset.UtcNow,
                correlationId = correlationId,
                details = new
                {
                    database = "connected",
                    cache = "available",
                    messageBroker = "available"
                }
            });
        }

        var problemDetails = new ProblemDetails
        {
            Title = "Service Unavailable",
            Detail = "Service is not ready for requests.",
            Instance = HttpContext.Request.Path,
            Status = StatusCodes.Status503ServiceUnavailable
        };
        problemDetails.Extensions["correlationId"] = correlationId;

        return StatusCode(StatusCodes.Status503ServiceUnavailable, problemDetails);
    }
}