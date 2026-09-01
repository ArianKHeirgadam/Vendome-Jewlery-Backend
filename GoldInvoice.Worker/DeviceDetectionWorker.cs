using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.SignalR;
using GoldInvoice.Domain.Platform;

namespace GoldInvoice.Worker;

public class DeviceDetectionWorker : BackgroundService
{
    private readonly ILogger<DeviceDetectionWorker> _logger;
    private readonly IPrinterDiscoveryService _printerDiscoveryService;
    private readonly IScannerDiscoveryService _scannerDiscoveryService;
    private readonly IDeviceRepository _deviceRepository;
    private readonly IHubContext<DeviceHub> _hubContext;

    public DeviceDetectionWorker(
        ILogger<DeviceDetectionWorker> logger,
        IPrinterDiscoveryService printerDiscoveryService,
        IScannerDiscoveryService scannerDiscoveryService,
        IDeviceRepository deviceRepository,
        IHubContext<DeviceHub> hubContext)
    {
        _logger = logger;
        _printerDiscoveryService = printerDiscoveryService;
        _scannerDiscoveryService = scannerDiscoveryService;
        _deviceRepository = deviceRepository;
        _hubContext = hubContext;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var printers = await _printerDiscoveryService.DiscoverPrintersAsync();
                var scanners = await _scannerDiscoveryService.DiscoverScannersAsync();

                foreach (var device in printers.Concat(scanners))
                {
                    await _deviceRepository.RegisterOrUpdateDeviceAsync(device);
                    await _hubContext.Clients.All.SendAsync("DeviceUpdated", device);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error detecting devices");
            }

            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }
}