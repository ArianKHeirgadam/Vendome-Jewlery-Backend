namespace GoldInvoice.Domain.Invoicing;

public static class InvoicePrintLeasing
{
    public static DateTimeOffset CalculateLeaseExpiry(DateTimeOffset now, TimeSpan duration) =>
        now.Add(duration);

    public static bool CanClaim(InvoicePrintStatus status, Guid? claimedByDeviceId, DateTimeOffset? leaseExpiresAt, Guid deviceId, DateTimeOffset now) =>
        status == InvoicePrintStatus.Requested &&
        (claimedByDeviceId is null || leaseExpiresAt is null || leaseExpiresAt <= now || claimedByDeviceId == deviceId);
}
