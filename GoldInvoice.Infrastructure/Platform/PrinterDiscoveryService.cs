using GoldInvoice.Domain.Platform;

namespace GoldInvoice.Infrastructure.Platform;

public class PrinterDiscoveryService : IPrinterDiscoveryService
{
    public Task<List<DesktopDevice>> DiscoverPrintersAsync()
    {
        var devices = new List<DesktopDevice>
        {
            new(Guid.NewGuid(), "printer1", "Test Printer"),
            new(Guid.NewGuid(), "printer2", "Another Printer")
        };

        return Task.FromResult(devices);
    }
}
