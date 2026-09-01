using GoldInvoice.Contracts.Devices;

namespace GoldInvoice.Application.Devices;

public interface IDeviceSynchronizationService
{
    Task<DeviceSynchronizationResult> SynchronizeAsync(
        Guid userId,
        IReadOnlyCollection<DeviceSnapshotRequest> devices,
        CancellationToken cancellationToken = default);
}
