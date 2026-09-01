using GoldInvoice.Domain.Platform;

namespace GoldInvoice.Infrastructure.Platform;

public sealed class PrinterDiscoveryService : IPrinterDiscoveryService
{
    // Hardware enumeration is performed by the Windows desktop host.
    // This adapter remains only as the stable domain port.
    public Task<List<DesktopDevice>> DiscoverPrintersAsync() =>
        Task.FromResult(new List<DesktopDevice>());
}
