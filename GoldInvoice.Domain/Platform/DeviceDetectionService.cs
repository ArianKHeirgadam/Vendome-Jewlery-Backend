using GoldInvoice.Domain.Platform;

namespace GoldInvoice.Domain.Platform
{
    public class DeviceDetectionService
    {
        private readonly IPrinterDiscoveryService _printerDiscoveryService;
        private readonly IScannerDiscoveryService _scannerDiscoveryService;

        public DeviceDetectionService(IPrinterDiscoveryService printerDiscoveryService, IScannerDiscoveryService scannerDiscoveryService)
        {
            _printerDiscoveryService = printerDiscoveryService;
            _scannerDiscoveryService = scannerDiscoveryService;
        }

        public async Task<List<DesktopDevice>> DetectPrintersAsync()
        {
            return await _printerDiscoveryService.DiscoverPrintersAsync();
        }

        public async Task<List<DesktopDevice>> DetectScannersAsync()
        {
            return await _scannerDiscoveryService.DiscoverScannersAsync();
        }
    }
}