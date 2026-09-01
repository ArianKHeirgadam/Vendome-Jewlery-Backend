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
            using var cts = new CancellationTokenSource();
            await worker.StartAsync(cts.Token);
            await Task.Delay(1000);
            cts.Cancel();
            await worker.StopAsync(CancellationToken.None);

            // Assert
            // Verify that devices were registered and notifications were sent.
        }
    }

    public class MockPrinterDiscoveryService : IPrinterDiscoveryService
    {
        public Task<List<DesktopDevice>> DiscoverPrintersAsync()
        {
            return Task.FromResult(new List<DesktopDevice>
            {
                new DesktopDevice(Guid.NewGuid(), "printer1", "Printer 1")
            });
        }
    }

    public class MockScannerDiscoveryService : IScannerDiscoveryService
    {
        public Task<List<DesktopDevice>> DiscoverScannersAsync()
        {
            return Task.FromResult(new List<DesktopDevice>
            {
                new DesktopDevice(Guid.NewGuid(), "scanner1", "Scanner 1")
            });
        }
    }

    public class MockHubContext : IHubContext<DeviceHub>
    {
        public IHubClients Clients => new MockHubClients();
        public IGroupManager Groups => new MockGroupManager();

        public class MockHubClients : IHubClients
        {
            public IClientProxy All => new MockClientProxy();
            public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotImplementedException();
            public IClientProxy Client(string connectionId) => throw new NotImplementedException();
            public IClientProxy Clients(IReadOnlyList<string> connectionIds) => throw new NotImplementedException();
            public IClientProxy Group(string groupName) => throw new NotImplementedException();
            public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotImplementedException();
            public IClientProxy Groups(IReadOnlyList<string> groupNames) => throw new NotImplementedException();
            public IClientProxy OthersInGroup(string connectionId, string groupName) => throw new NotImplementedException();
            public IClientProxy User(string userId) => throw new NotImplementedException();
            public IClientProxy Users(IReadOnlyList<string> userIds) => throw new NotImplementedException();
        }

        public class MockClientProxy : IClientProxy
        {
            public Task SendCoreAsync(string method, object?[]? args, CancellationToken cancellationToken = default)
                => Task.CompletedTask;
        }

        public class MockGroupManager : IGroupManager
        {
            public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
                => Task.CompletedTask;

            public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
                => Task.CompletedTask;
        }
    }
}