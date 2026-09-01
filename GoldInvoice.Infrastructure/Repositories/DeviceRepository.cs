using GoldInvoice.Domain.Platform;
using GoldInvoice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoldInvoice.Infrastructure.Repositories;

public class DeviceRepository : IDeviceRepository
{
    private readonly GoldInvoiceDbContext _context;

    public DeviceRepository(GoldInvoiceDbContext context)
    {
        _context = context;
    }

    public async Task RegisterOrUpdateDeviceAsync(DesktopDevice device)
    {
        var existingDevice = await _context.DesktopDevices
            .FirstOrDefaultAsync(d => d.DeviceIdentifierHash == device.DeviceIdentifierHash);

        if (existingDevice != null)
        {
            existingDevice.Refresh(device.DisplayName, DateTimeOffset.UtcNow);
        }
        else
        {
            _context.DesktopDevices.Add(device);
        }

        await _context.SaveChangesAsync();
    }
}
