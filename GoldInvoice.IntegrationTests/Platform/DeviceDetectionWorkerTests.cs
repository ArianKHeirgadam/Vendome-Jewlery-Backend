using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using GoldInvoice.Domain.Platform;
using GoldInvoice.Worker;

namespace GoldInvoice.IntegrationTests.Platform;

public class DeviceDetectionWorkerTests
{
    [Fact]
    public async Task DeviceDetectionWorker_RegistersDevices_WhenDevicesAreDetected()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDeviceRepository, MockDeviceRepository>();
        services.AddSingleton<IPrinterDiscoveryService, MockPrinterDiscoveryService>();
        services.AddSingleton<IScannerDiscoveryService, MockScannerDiscoveryService>();
        services.AddSingleton<IHubContext<DeviceHub>, MockHubContext>();

        await using var serviceProvider = services.BuildServiceProvider();

        var worker = new DeviceDetectionWorker(
            serviceProvider.GetRequiredService<ILogger<DeviceDetectionWorker>>(),
            serviceProvider.GetRequiredService<IPrinterDiscoveryService>(),
            serviceProvider.GetRequiredService<IScannerDiscoveryService>(),
            serviceProvider.GetRequiredService<IDeviceRepository>(),
            serviceProvider.GetRequiredService<IHubContext<DeviceHub>>());

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));

        await worker.StartAsync(cts.Token);
        try
        {
            await Task.Delay(50, CancellationToken.None);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        var repository = (MockDeviceRepository)serviceProvider.GetRequiredService<IDeviceRepository>();
        Assert.Equal(2, repository.RegisteredDevices.Count);
    }
}

internal sealed class MockDeviceRepository : IDeviceRepository
{
    public List<DesktopDevice> RegisteredDevices { get; } = new();

    public Task RegisterOrUpdateDeviceAsync(DesktopDevice device)
    {
        RegisteredDevices.Add(device);
        return Task.CompletedTask;
    }
}

internal sealed class MockPrinterDiscoveryService : IPrinterDiscoveryService
{
    public Task<List<DesktopDevice>> DiscoverPrintersAsync() =>
        Task.FromResult(new List<DesktopDevice>
        {
            new(Guid.NewGuid(), "printer1", "Printer 1")
        });
}

internal sealed class MockScannerDiscoveryService : IScannerDiscoveryService
{
    public Task<List<DesktopDevice>> DiscoverScannersAsync() =>
        Task.FromResult(new List<DesktopDevice>
        {
            new(Guid.NewGuid(), "scanner1", "Scanner 1")
        });
}

internal sealed class MockHubContext : IHubContext<DeviceHub>
{
    public IHubClients Clients { get; } = new MockHubClients();
    public IGroupManager Groups { get; } = new MockGroupManager();

    private sealed class MockHubClients : IHubClients
    {
        public IClientProxy All { get; } = new MockClientProxy();
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => All;
        public IClientProxy Client(string connectionId) => All;
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => All;
        public IClientProxy Group(string groupName) => All;
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => All;
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => All;
        public IClientProxy OthersInGroup(string connectionId, string groupName) => All;
        public IClientProxy User(string userId) => All;
        public IClientProxy Users(IReadOnlyList<string> userIds) => All;
    }

    private sealed class MockClientProxy : IClientProxy
    {
        public Task SendCoreAsync(string method, object?[]? args, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class MockGroupManager : IGroupManager
    {
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
