using GoldInvoice.Domain.Payments;
using GoldInvoice.Domain.Platform;
using GoldInvoice.Infrastructure.Configuration;
using GoldInvoice.Infrastructure.Integration;
using GoldInvoice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GoldInvoice.IntegrationTests;

public sealed class DataRetentionTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-08-01T12:00:00+00:00");

    [Fact]
    public async Task Sweep_PurgesOldProcessedOutboxButKeepsFreshAndInFlight()
    {
        await using var context = CreateContext();
        var lockId = Guid.NewGuid();
        var oldProcessed = new OutboxMessage("OrderStatusChanged.v1", "{}", Now);
        oldProcessed.Claim(lockId, Now.AddMinutes(5), Now);
        oldProcessed.MarkProcessed(lockId, Now.AddDays(-40));

        var freshProcessed = new OutboxMessage("OrderStatusChanged.v1", "{}", Now);
        freshProcessed.Claim(lockId, Now.AddMinutes(5), Now);
        freshProcessed.MarkProcessed(lockId, Now);

        var oldPending = new OutboxMessage("OrderStatusChanged.v1", "{}", Now.AddDays(-40));
        context.OutboxMessages.AddRange(oldProcessed, freshProcessed, oldPending);
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var result = await service.SweepAsync(CancellationToken.None);

        Assert.Equal(1, result.PurgedOutboxMessages);
        Assert.Equal(1, await context.OutboxMessages.CountAsync(message => message.Status == OutboxMessageStatus.Processed));
        Assert.Equal(1, await context.OutboxMessages.CountAsync(message => message.Status == OutboxMessageStatus.Pending));
        Assert.False(await context.OutboxMessages.AnyAsync(message => message.Id == oldProcessed.Id));
    }

    [Fact]
    public async Task Sweep_PurgesOldCallbacksButKeepsRecentOnes()
    {
        await using var context = CreateContext();
        var oldCallback = new PaymentCallback(
            "ZARINPAL", "cb-old", "hash-old", Now.AddDays(-120), null, isVerified: true, "PAYMENT_VERIFIED", null);
        var freshCallback = new PaymentCallback(
            "ZARINPAL", "cb-fresh", "hash-fresh", Now.AddDays(-1), null, isVerified: true, "PAYMENT_VERIFIED", null);
        context.PaymentCallbacks.AddRange(oldCallback, freshCallback);
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var result = await service.SweepAsync(CancellationToken.None);

        Assert.Equal(1, result.PurgedPaymentCallbacks);
        Assert.False(await context.PaymentCallbacks.AnyAsync(callback => callback.Id == oldCallback.Id));
        Assert.True(await context.PaymentCallbacks.AnyAsync(callback => callback.Id == freshCallback.Id));
    }

    [Fact]
    public async Task Sweep_PurgesOnlyCompletedExpiredIdempotencyRecords()
    {
        await using var context = CreateContext();
        var completedExpired = new IdempotencyRecord("Orders.Create", "a", "a", Now.AddHours(-30));
        completedExpired.Complete(201, "order-id", Now.AddHours(-30));

        var completedFresh = new IdempotencyRecord("Orders.Create", "b", "b", Now.AddHours(24));
        completedFresh.Complete(201, "order-id", Now);

        // In-flight record must never be purged even when long past its expiry.
        var inFlightOld = new IdempotencyRecord("Orders.Create", "c", "c", Now.AddHours(-30));

        context.IdempotencyRecords.AddRange(completedExpired, completedFresh, inFlightOld);
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var result = await service.SweepAsync(CancellationToken.None);

        Assert.Equal(1, result.PurgedIdempotencyRecords);
        Assert.False(await context.IdempotencyRecords.AnyAsync(record => record.Id == completedExpired.Id));
        Assert.True(await context.IdempotencyRecords.AnyAsync(record => record.Id == completedFresh.Id));
        Assert.True(await context.IdempotencyRecords.AnyAsync(record => record.Id == inFlightOld.Id));
    }

    private static DataRetentionService CreateService(GoldInvoiceDbContext context) => new(
        context,
        Options.Create(new RetentionOptions
        {
            OutboxProcessedRetentionDays = 30,
            CallbackLogRetentionDays = 90,
            PurgeExpiredIdempotencyRecords = true,
            SweepIntervalHours = 24,
            MaximumBatchSize = 100
        }),
        new FixedTimeProvider(Now),
        NullLogger<DataRetentionService>.Instance);

    private static GoldInvoiceDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<GoldInvoiceDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new GoldInvoiceDbContext(options);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}