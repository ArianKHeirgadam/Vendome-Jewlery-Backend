using GoldInvoice.Domain.Common;
using GoldInvoice.Domain.Platform;

namespace GoldInvoice.UnitTests;

public sealed class PhaseSixDomainTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-08-10T20:00:00+00:00");

    [Fact]
    public void OutboxMessage_RequiresOwnedLockForCompletionAndSupportsHeartbeat()
    {
        var message = CreateMessage();
        var lockId = Guid.NewGuid();
        message.Claim(lockId, Now.AddMinutes(1), Now);

        Assert.Throws<DomainConflictException>(() =>
            message.MarkProcessed(Guid.NewGuid(), Now.AddSeconds(1)));

        message.RenewLock(lockId, Now.AddMinutes(2), Now.AddSeconds(10));
        message.MarkProcessed(lockId, Now.AddSeconds(20));

        Assert.Equal(OutboxMessageStatus.Processed, message.Status);
        Assert.Equal(Now.AddSeconds(20), message.ProcessedAt);
        Assert.Null(message.LockId);
        Assert.Null(message.LockedUntil);
    }

    [Fact]
    public void OutboxMessage_ExpiredLockCanBeRecoveredByAnotherDispatcher()
    {
        var message = CreateMessage();
        var abandonedLock = Guid.NewGuid();
        var recoveryLock = Guid.NewGuid();
        message.Claim(abandonedLock, Now.AddSeconds(10), Now);

        message.Claim(recoveryLock, Now.AddMinutes(1), Now.AddSeconds(11));

        Assert.Equal(OutboxMessageStatus.Processing, message.Status);
        Assert.Equal(recoveryLock, message.LockId);
        Assert.Throws<DomainConflictException>(() =>
            message.MarkProcessed(abandonedLock, Now.AddSeconds(12)));
    }

    [Fact]
    public void OutboxMessage_RetryDeadLetterAndAuditedReprocessPreserveAttemptCount()
    {
        var message = CreateMessage();
        var firstLock = Guid.NewGuid();
        message.Claim(firstLock, Now.AddMinutes(1), Now);
        message.MarkFailed(
            firstLock,
            "Transient delivery failure (TimeoutException).",
            Now.AddSeconds(1),
            Now.AddSeconds(6),
            deadLetter: false);
        Assert.Equal(1, message.RetryCount);
        Assert.Equal(OutboxMessageStatus.Failed, message.Status);

        var secondLock = Guid.NewGuid();
        message.Claim(secondLock, Now.AddMinutes(2), Now.AddSeconds(6));
        message.MarkFailed(
            secondLock,
            "The message contract is invalid.",
            Now.AddSeconds(7),
            nextRetryAt: null,
            deadLetter: true);
        Assert.Equal(2, message.RetryCount);
        Assert.Equal(OutboxMessageStatus.DeadLetter, message.Status);

        message.Reprocess(Now.AddMinutes(1));
        Assert.Equal(2, message.RetryCount);
        Assert.Equal(OutboxMessageStatus.Pending, message.Status);
        Assert.Equal("The message contract is invalid.", message.LastError);
    }

    [Fact]
    public void OutboxMessage_GracefulReleaseDoesNotConsumeRetryAttempt()
    {
        var message = CreateMessage();
        var lockId = Guid.NewGuid();
        message.Claim(lockId, Now.AddMinutes(1), Now);

        message.ReleaseClaim(lockId, Now.AddSeconds(5), Now.AddSeconds(5));

        Assert.Equal(0, message.RetryCount);
        Assert.Equal(OutboxMessageStatus.Failed, message.Status);
        Assert.True(message.CanBeClaimed(Now.AddSeconds(5)));
    }

    private static OutboxMessage CreateMessage() => new(
        "invoice.created.v1",
        "{\"safe\":true}",
        Now);
}
