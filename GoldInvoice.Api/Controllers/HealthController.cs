using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System;

namespace GoldInvoice.Api.Controllers;

[ApiController]
[Route("health")]
public class HealthController : ControllerBase
{
    private readonly HealthCheckService _healthCheckService;

    public HealthController(HealthCheckService healthCheckService)
    {
        _healthCheckService = healthCheckService;
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
            correlationId
        });
    }

    [HttpGet("ready")]
    public async Task<IActionResult> GetReady(CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        Response.Headers["X-Correlation-ID"] = correlationId;

        var healthReport = await _healthCheckService.CheckHealthAsync(cancellationToken);
        var isReady = healthReport.Status == HealthStatus.Healthy;

        if (isReady)
        {
            return Ok(new
            {
                status = "ready",
                timestamp = DateTimeOffset.UtcNow,
                correlationId,
                details = healthReport.Entries.ToDictionary(
                    entry => entry.Key,
                    entry => new
                    {
                        status = entry.Value.Status.ToString(),
                        description = entry.Value.Description,
                        durationMs = entry.Value.Duration.TotalMilliseconds
                    })
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
        problemDetails.Extensions["healthStatus"] = healthReport.Status.ToString();

        return StatusCode(StatusCodes.Status503ServiceUnavailable, problemDetails);
    }
}
