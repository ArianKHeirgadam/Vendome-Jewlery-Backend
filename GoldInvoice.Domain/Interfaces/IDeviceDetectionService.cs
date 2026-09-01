namespace GoldInvoice.Domain.Platform;

public interface IPrinterDiscoveryService
{
    Task<List<DesktopDevice>> DiscoverPrintersAsync();
}

public interface IScannerDiscoveryService
{
    Task<List<DesktopDevice>> DiscoverScannersAsync();
}