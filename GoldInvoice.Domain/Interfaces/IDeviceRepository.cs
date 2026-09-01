namespace GoldInvoice.Domain.Platform;

public interface IDeviceRepository
{
    Task RegisterOrUpdateDeviceAsync(DesktopDevice device);
}