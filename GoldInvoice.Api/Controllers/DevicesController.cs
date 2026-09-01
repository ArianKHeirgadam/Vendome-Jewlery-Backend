using System.Security.Claims;
using GoldInvoice.Application.Devices;
using GoldInvoice.Contracts.Devices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GoldInvoice.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/devices")]
public sealed class DevicesController : ControllerBase
{
    private readonly IDeviceSynchronizationService _service;

    public DevicesController(IDeviceSynchronizationService service) => _service = service;

    [HttpPost("sync")]
    public async Task<ActionResult<DeviceSynchronizationResult>> Synchronize(
        [FromBody] IReadOnlyCollection<DeviceSnapshotRequest> devices,
        CancellationToken cancellationToken)
    {
        if (devices.Count > 100)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Too many devices.",
                Detail = "A synchronization request may contain at most 100 devices.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(claim, out var userId) || userId == Guid.Empty) return Unauthorized();

        return Ok(await _service.SynchronizeAsync(userId, devices, cancellationToken));
    }
}
