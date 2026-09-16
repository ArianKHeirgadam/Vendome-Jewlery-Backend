using GoldInvoice.Domain.Invoicing;

namespace GoldInvoice.UnitTests.PrintAgent;

public sealed class InvoicePrintLeasePolicyTests
{
    [Fact]
    public void Requested_without_active_lease_can_be_claimed()
    {
        var deviceId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        Assert.True(InvoicePrintLeasePolicy.IsClaimAvailable(InvoicePrintStatus.Requested, null, null, deviceId, now));
    }

    [Fact]
    public void Active_lease_owned_by_other_device_cannot_be_claimed()
    {
        var deviceId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        Assert.False(InvoicePrintLeasePolicy.IsClaimAvailable(
            InvoicePrintStatus.Requested,
            Guid.NewGuid(),
            now.AddMinutes(1),
            deviceId,
            now));
    }

    [Fact]
    public void Expired_lease_can_be_reclaimed()
    {
        var deviceId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        Assert.True(InvoicePrintLeasePolicy.IsClaimAvailable(
            InvoicePrintStatus.Requested,
            Guid.NewGuid(),
            now.AddSeconds(-1),
            deviceId,
            now));
    }
}
