namespace VendomeJewleryDesktopApp.Services;

internal sealed record DeviceDetectionSettings(TimeSpan PollInterval)
{
    public static DeviceDetectionSettings Default => new(TimeSpan.FromSeconds(10));
}
