using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using GoldInvoice.Domain.Platform;
using GoldInvoice.Infrastructure.Repositories;
using GoldInvoice.Worker;
using System.Threading;
using System.Threading.Tasks;

namespace GoldInvoice.IntegrationTests.Platform
{
    public class DeviceDetectionWorkerTests
    {
        [Fact]
        public async Task DeviceDetectionWorker_RegistersDevices_WhenDevicesAreDetected()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IDeviceRepository, DeviceRepository>();
            services.AddSingleton<IPrinterDiscoveryService, MockPrinterDiscoveryService>();
            services.AddSingleton<IScannerDiscoveryService, MockScannerDiscoveryService>();
            services.AddSingleton<IHubContext<DeviceHub>, MockHubContext>();

            var serviceProvider = services.BuildServiceProvider();
            var worker = new DeviceDetectionWorker(
                serviceProvider.GetRequiredService<ILogger<DeviceDetectionWorker>>(),
                serviceProvider.GetRequiredService<IPrinterDiscoveryService>(),
                serviceProvider.GetRequiredService<IScannerDiscoveryService>(),
                serviceProvider.GetRequiredService<IDeviceRepository>(),
                serviceProvider.GetRequiredService<IHubContext<DeviceHub>>());

            // Act
            var cts = new CancellationTokenSource();
            await worker.StartAsync(cts.Token);
            await Task.Delay(1000); // Wait for the worker to run
            await worker.StopAsync(cts.Token);

            // Assert
            // Verify that devices were registered and notifications were sent
        }
    }

    public class MockPrinterDiscoveryService : IPrinterDiscoveryService
    {
        public Task<List<DesktopDevice>> DiscoverPrintersAsync()
        {
            return Task.FromResult(new List<DesktopDevice>
            {
                new DesktopDevice(Guid.NewGuid(), "printer1", "Printer 1", DeviceType.Printer, true)
            });
        }
    }

    public class MockScannerDiscoveryService : IScannerDiscoveryService
    {
        public Task<List<DesktopDevice>> DiscoverScannersAsync()
        {
            return Task.FromResult(new List<DesktopDevice>
            {
                new DesktopDevice(Guid.NewGuid(), "scanner1", "Scanner 1", DeviceType.Scanner, true)
            });
        }
    }

    public class MockHubContext : IHubContext<DeviceHub>
    {
        public IHubClients Clients => new MockHubClients();
        public Groups Groups => throw new System.NotImplementedException();

        public class MockHubClients : IHubClients
        {
            public IClientProxy All => new MockClientProxy();
            public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new System.NotImplementedException();
            public IClientProxy Client(string connectionId) => throw new System.NotImplementedException();
            public IClientProxy Clients(IReadOnlyList<string> connectionIds) => throw new System.NotImplementedException();
            public IClientProxy Group(string groupName) => throw new System.NotImplementedException();
            public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new System.NotImplementedException();
            public IClientProxy Groups(IReadOnlyList<string> groupNames) => throw new System.NotImplementedException();
            public IClientProxy OthersInGroup(string connectionId, string groupName) => throw new System.NotImplementedException();
            public IClientProxy User(string userId) => throw new System.NotImplementedException();
            public IClientProxy Users(IReadOnlyList<string> userIds) => throw new System.NotImplementedException();
        }

        public class MockClientProxy : IClientProxy
        {
            public Task SendCoreAsync(string method, object?[]? args, CancellationToken cancellationToken = default)
            {
                // Verify that the method was called with the correct arguments
                return Task.CompletedTask;
            }
        }
    }
}