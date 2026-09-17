namespace GoldInvoice.Domain.Invoicing;

public static class InvoicePrintLeasePolicy
{
    public static bool IsClaimAvailable(
        InvoicePrintStatus status,
        Guid? claimedByDeviceId,
        DateTimeOffset? leaseExpiresAt,
        Guid deviceId,
        DateTimeOffset now)
    {
        if (status != InvoicePrintStatus.Requested)
            return false;
        if (claimedByDeviceId is null || leaseExpiresAt is null || leaseExpiresAt <= now)
            return true;
        return claimedByDeviceId == deviceId;
    }

    public static DateTimeOffset ExpireAt(DateTimeOffset now) => now.AddMinutes(3);
}
