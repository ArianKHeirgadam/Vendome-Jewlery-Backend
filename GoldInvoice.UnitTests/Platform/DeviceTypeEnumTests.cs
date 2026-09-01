using GoldInvoice.Domain.Platform;
using Xunit;

namespace GoldInvoice.UnitTests.Platform;

public sealed class DeviceTypeEnumTests
{
    [Theory]
    [InlineData(DeviceType.Printer)]
    [InlineData(DeviceType.Scanner)]
    public void SupportedDeviceTypes_AreDefined(DeviceType type)
    {
        Assert.NotEqual(DeviceType.Unknown, type);
    }
}
