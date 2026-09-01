using GoldInvoice.Domain.Platform;

namespace GoldInvoice.Infrastructure.Platform;

public sealed class DeviceDetectionService
{
    private readonly IPrinterDiscoveryService _printerDiscoveryService;
    private readonly IScannerDiscoveryService _scannerDiscoveryService;

    public DeviceDetectionService(IPrinterDiscoveryService printerDiscoveryService, IScannerDiscoveryService scannerDiscoveryService)
    {
        _printerDiscoveryService = printerDiscoveryService;
        _scannerDiscoveryService = scannerDiscoveryService;
    }

    public Task<List<DesktopDevice>> DetectPrintersAsync() => _printerDiscoveryService.DiscoverPrintersAsync();
    public Task<List<DesktopDevice>> DetectScannersAsync() => _scannerDiscoveryService.DiscoverScannersAsync();
}
