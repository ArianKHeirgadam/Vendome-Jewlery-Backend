using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using GoldInvoice.Domain.Platform;

namespace GoldInvoice.Api.Hubs;

[Authorize]
public class DeviceHub : Hub
{
    private readonly IDeviceRepository _deviceRepository;

    public DeviceHub(IDeviceRepository deviceRepository)
    {
        _deviceRepository = deviceRepository;
    }

    public async Task RegisterDevice(DesktopDevice device)
    {
        await _deviceRepository.RegisterOrUpdateDeviceAsync(device);
    }
}
