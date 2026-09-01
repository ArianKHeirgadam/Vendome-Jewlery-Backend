using GoldInvoice.Domain.Platform;

namespace GoldInvoice.Infrastructure.Platform;

public class ScannerDiscoveryService : IScannerDiscoveryService
{
    public Task<List<DesktopDevice>> DiscoverScannersAsync()
    {
        var devices = new List<DesktopDevice>
        {
            new(Guid.NewGuid(), "scanner1", "Test Scanner")
        };

        return Task.FromResult(devices);
    }
}
