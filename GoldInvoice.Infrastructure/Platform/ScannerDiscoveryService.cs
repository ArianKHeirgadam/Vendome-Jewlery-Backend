using GoldInvoice.Domain.Platform;

namespace GoldInvoice.Infrastructure.Platform
{
    public class ScannerDiscoveryService : IScannerDiscoveryService
    {
        public async Task<List<DesktopDevice>> DiscoverScannersAsync()
        {
            var devices = new List<DesktopDevice>();
            try
            {
                // Mock implementation for demonstration
                devices.Add(new DesktopDevice(Guid.Empty, "scanner1", "Test Scanner", DeviceType.Scanner, true));
            }
            catch (Exception ex)
            {
                // Log error
            }
            return devices;
        }
    }
}