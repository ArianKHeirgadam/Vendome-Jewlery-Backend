using Moq;
using Xunit;
using GoldInvoice.Domain.Platform;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GoldInvoice.UnitTests.Platform
{
    public class DeviceDetectionServiceTests
    {
        [Fact]
        public async Task DetectPrintersAsync_ReturnsEmptyList_WhenNoPrintersFound()
        {
            // Arrange
            var mockPrinterService = new Mock<IPrinterDiscoveryService>();
            mockPrinterService.Setup(s => s.DiscoverPrintersAsync()).ReturnsAsync(new List<DesktopDevice>());

            var mockScannerService = new Mock<IScannerDiscoveryService>();
            var service = new DeviceDetectionService(mockPrinterService.Object, mockScannerService.Object);

            // Act
            var printers = await service.DetectPrintersAsync();

            // Assert
            Assert.NotNull(printers);
            Assert.Empty(printers);
        }

        [Fact]
        public async Task DetectScannersAsync_ReturnsEmptyList_WhenNoScannersFound()
        {
            // Arrange
            var mockPrinterService = new Mock<IPrinterDiscoveryService>();
            var mockScannerService = new Mock<IScannerDiscoveryService>();
            mockScannerService.Setup(s => s.DiscoverScannersAsync()).ReturnsAsync(new List<DesktopDevice>());

            var service = new DeviceDetectionService(mockPrinterService.Object, mockScannerService.Object);

            // Act
            var scanners = await service.DetectScannersAsync();

            // Assert
            Assert.NotNull(scanners);
            Assert.Empty(scanners);
        }

        [Fact]
        public async Task DetectPrintersAsync_ReturnsListOfPrinters_WhenPrintersFound()
        {
            // Arrange
            var mockPrinterService = new Mock<IPrinterDiscoveryService>();
            var mockPrinters = new List<DesktopDevice>
            {
                new DesktopDevice(Guid.NewGuid(), "printer1", "Printer 1")
            };
            mockPrinterService.Setup(s => s.DiscoverPrintersAsync()).ReturnsAsync(mockPrinters);

            var mockScannerService = new Mock<IScannerDiscoveryService>();
            var service = new DeviceDetectionService(mockPrinterService.Object, mockScannerService.Object);

            // Act
            var printers = await service.DetectPrintersAsync();

            // Assert
            Assert.NotNull(printers);
            Assert.Single(printers);
            Assert.Equal("Printer 1", printers[0].DisplayName);
        }

        [Fact]
        public async Task DetectScannersAsync_ReturnsListOfScanners_WhenScannersFound()
        {
            // Arrange
            var mockPrinterService = new Mock<IPrinterDiscoveryService>();
            var mockScannerService = new Mock<IScannerDiscoveryService>();
            var mockScanners = new List<DesktopDevice>
            {
                new DesktopDevice(Guid.NewGuid(), "scanner1", "Scanner 1")
            };
            mockScannerService.Setup(s => s.DiscoverScannersAsync()).ReturnsAsync(mockScanners);

            var service = new DeviceDetectionService(mockPrinterService.Object, mockScannerService.Object);

            // Act
            var scanners = await service.DetectScannersAsync();

            // Assert
            Assert.NotNull(scanners);
            Assert.Single(scanners);
            Assert.Equal("Scanner 1", scanners[0].DisplayName);
        }
    }
}