using GoldInvoice.Domain.Platform;
using Xunit;

namespace GoldInvoice.UnitTests.Platform;

public sealed class DesktopDeviceTests
{
    [Fact]
    public void Refresh_MarksDeviceOnline()
    {
        var device = new DesktopDevice(Guid.NewGuid(), "hash", "Old", DeviceType.Printer, "Old Model");
        device.MarkOffline();
        var seenAt = DateTimeOffset.UtcNow;

        device.Refresh("New", DeviceType.Printer, "New Model", seenAt);

        Assert.True(device.IsOnline);
        Assert.Equal("New", device.DisplayName);
        Assert.Equal("New Model", device.Model);
        Assert.Equal(seenAt, device.LastSeenAt);
    }

    [Fact]
    public void MarkOffline_DoesNotRevokeDevice()
    {
        var device = new DesktopDevice(Guid.NewGuid(), "hash", "Scanner", DeviceType.Scanner, "Model");
        device.MarkOffline();

        Assert.False(device.IsOnline);
        Assert.True(device.IsActive);
        Assert.Null(device.RevokedAt);
    }
}
