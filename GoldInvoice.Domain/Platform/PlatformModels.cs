using GoldInvoice.Domain.Common;

namespace GoldInvoice.Domain.Platform;

public enum OutboxMessageStatus
{
    Pending,
    Processing,
    Processed,
    Failed,
    DeadLetter
}

public enum IdempotencyRecordStatus
{
    Processing,
    Completed,
    Failed
}

public sealed class DesktopDevice : AuditableEntity
{
    private DesktopDevice()
    {
    }

    public DesktopDevice(
        Guid registeredByUserId,
        string deviceIdentifierHash,
        string displayName,
        string? publicKeyPem = null,
        string? publicKeyThumbprint = null)
    {
        Guard.AgainstEmpty(registeredByUserId, nameof(registeredByUserId));
        RegisteredByUserId = registeredByUserId;
        DeviceIdentifierHash = Guard.Required(deviceIdentifierHash, nameof(deviceIdentifierHash), 128);
        DisplayName = Guard.Required(displayName, nameof(displayName), 200);
        PublicKeyPem = Guard.Optional(publicKeyPem, nameof(publicKeyPem), 4000);
        PublicKeyThumbprint = Guard.Optional(publicKeyThumbprint, nameof(publicKeyThumbprint), 128);
    }

    public Guid RegisteredByUserId { get; private set; }

    public string DeviceIdentifierHash { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    public string? PublicKeyPem { get; private set; }

    public string? PublicKeyThumbprint { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset? ApprovedAt { get; private set; }

    public DateTimeOffset? LastSeenAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public void Approve(DateTimeOffset approvedAt)
    {
        Guard.AgainstDefault(approvedAt, nameof(approvedAt));
        if (IsActive || ApprovedAt is not null || RevokedAt is not null)
        {
            throw new DomainConflictException("Only a pending device can be approved.");
        }

        ApprovedAt = approvedAt;
        IsActive = true;
    }

    public void Revoke(DateTimeOffset revokedAt)
    {
        Guard.AgainstDefault(revokedAt, nameof(revokedAt));
        if (RevokedAt is not null)
        {
            throw new DomainConflictException("The device is already revoked.");
        }

        RevokedAt = revokedAt;
        IsActive = false;
    }

    public void Heartbeat(DateTimeOffset seenAt)
    {
        Guard.AgainstDefault(seenAt, nameof(seenAt));
        if (!IsActive || RevokedAt is not null)
        {
            throw new DomainConflictException("Only an approved device can report a heartbeat.");
        }

        LastSeenAt = seenAt;
    }

    public void BindThumbprint(string publicKeyThumbprint)
    {
        PublicKeyThumbprint = Guard.Required(publicKeyThumbprint, nameof(publicKeyThumbprint), 128);
    }
}

public sealed class OutboxMessage : AuditableEntity, IProtectedFromHardDelete
{
    private OutboxMessage()
    {
    }

    public OutboxMessage(string messageType, string payload, DateTimeOffset occurredAt)
    {
        MessageType = Guard.Required(messageType, nameof(messageType), 300);
        Payload = Guard.Required(payload, nameof(payload), int.MaxValue);
        Guard.AgainstDefault(occurredAt, nameof(occurredAt));
        OccurredAt = occurredAt;
    }

    public string MessageType { get; private set; } = string.Empty;

    public string Payload { get; private set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; private set; }

    public DateTimeOffset? ProcessedAt { get; private set; }

    public int RetryCount { get; private set; }

    public DateTimeOffset? NextRetryAt { get; private set; }

    public string? LastError { get; private set; }

    public OutboxMessageStatus Status { get; private set; } = OutboxMessageStatus.Pending;

    public Guid? LockId { get; private set; }

    public DateTimeOffset? LockedUntil { get; private set; }

    public bool CanBeClaimed(DateTimeOffset now) =>
        (Status is OutboxMessageStatus.Pending or OutboxMessageStatus.Failed &&
         (NextRetryAt is null || NextRetryAt <= now)) ||
        (Status == OutboxMessageStatus.Processing && LockedUntil <= now);

    public void Claim(Guid lockId, DateTimeOffset lockedUntil, DateTimeOffset now)
    {
        Guard.AgainstEmpty(lockId, nameof(lockId));
        Guard.AgainstDefault(lockedUntil, nameof(lockedUntil));
        Guard.AgainstDefault(now, nameof(now));
        if (!CanBeClaimed(now) || lockedUntil <= now)
        {
            throw new DomainConflictException("The outbox message cannot be claimed.");
        }

        Status = OutboxMessageStatus.Processing;
        LockId = lockId;
        LockedUntil = lockedUntil;
    }

    public void RenewLock(Guid lockId, DateTimeOffset lockedUntil, DateTimeOffset now)
    {
        EnsureLockOwner(lockId, now, requireUnexpiredLock: true);
        if (lockedUntil <= now)
        {
            throw new ArgumentOutOfRangeException(nameof(lockedUntil));
        }

        LockedUntil = lockedUntil;
    }

    public void MarkProcessed(Guid lockId, DateTimeOffset processedAt)
    {
        EnsureLockOwner(lockId, processedAt, requireUnexpiredLock: false);
        Status = OutboxMessageStatus.Processed;
        ProcessedAt = processedAt;
        NextRetryAt = null;
        LastError = null;
        LockId = null;
        LockedUntil = null;
    }

    public void MarkFailed(
        Guid lockId,
        string failure,
        DateTimeOffset failedAt,
        DateTimeOffset? nextRetryAt,
        bool deadLetter)
    {
        EnsureLockOwner(lockId, failedAt, requireUnexpiredLock: false);
        if (!deadLetter && (nextRetryAt is null || nextRetryAt <= failedAt))
        {
            throw new ArgumentOutOfRangeException(nameof(nextRetryAt));
        }

        RetryCount = checked(RetryCount + 1);
        LastError = Guard.Required(failure, nameof(failure), 4000);
        Status = deadLetter ? OutboxMessageStatus.DeadLetter : OutboxMessageStatus.Failed;
        NextRetryAt = deadLetter ? null : nextRetryAt;
        LockId = null;
        LockedUntil = null;
    }

    public void ReleaseClaim(Guid lockId, DateTimeOffset nextRetryAt, DateTimeOffset releasedAt)
    {
        EnsureLockOwner(lockId, releasedAt, requireUnexpiredLock: false);
        if (nextRetryAt < releasedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(nextRetryAt));
        }

        Status = OutboxMessageStatus.Failed;
        NextRetryAt = nextRetryAt;
        LockId = null;
        LockedUntil = null;
    }

    public void Reprocess(DateTimeOffset requestedAt)
    {
        Guard.AgainstDefault(requestedAt, nameof(requestedAt));
        if (Status != OutboxMessageStatus.DeadLetter)
        {
            throw new DomainConflictException("Only a dead-letter outbox message can be reprocessed.");
        }

        Status = OutboxMessageStatus.Pending;
        NextRetryAt = requestedAt;
        LockId = null;
        LockedUntil = null;
    }

    private void EnsureLockOwner(Guid lockId, DateTimeOffset now, bool requireUnexpiredLock)
    {
        Guard.AgainstEmpty(lockId, nameof(lockId));
        Guard.AgainstDefault(now, nameof(now));
        if (Status != OutboxMessageStatus.Processing || LockId != lockId ||
            (requireUnexpiredLock && LockedUntil <= now))
        {
            throw new DomainConflictException("The outbox processing lock is not owned by this dispatcher.");
        }
    }
}

public sealed class AuditLog : AuditableEntity, IAppendOnlyEntity, IProtectedFromHardDelete
{
    private AuditLog()
    {
    }

    public AuditLog(string action, string entityType, string entityId, DateTimeOffset occurredAt)
    {
        Action = Guard.Required(action, nameof(action), 200);
        EntityType = Guard.Required(entityType, nameof(entityType), 300);
        EntityId = Guard.Required(entityId, nameof(entityId), 200);
        Guard.AgainstDefault(occurredAt, nameof(occurredAt));
        OccurredAt = occurredAt;
    }

    public Guid? ActorUserId { get; private set; }

    public string Action { get; private set; } = string.Empty;

    public string EntityType { get; private set; } = string.Empty;

    public string EntityId { get; private set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; private set; }

    public string? CorrelationId { get; private set; }

    public string? IpAddress { get; private set; }

    public string? OldValuesJson { get; private set; }

    public string? NewValuesJson { get; private set; }

    public void SetContext(Guid? actorUserId, string? correlationId)
    {
        if (actorUserId == Guid.Empty)
        {
            throw new ArgumentException("A non-empty actor identifier is required.", nameof(actorUserId));
        }

        ActorUserId = actorUserId;
        CorrelationId = Guard.Optional(correlationId, nameof(correlationId), 128);
    }

    public void SetValues(string? oldValuesJson, string? newValuesJson)
    {
        OldValuesJson = Guard.Optional(oldValuesJson, nameof(oldValuesJson), int.MaxValue);
        NewValuesJson = Guard.Optional(newValuesJson, nameof(newValuesJson), int.MaxValue);
    }
}

public sealed class SystemSetting : AuditableEntity
{
    private SystemSetting()
    {
    }

    public SystemSetting(string key, string dataType, string? value, string? secretReference)
    {
        if (string.IsNullOrWhiteSpace(value) == string.IsNullOrWhiteSpace(secretReference))
        {
            throw new ArgumentException("Exactly one of value or secret reference must be provided.");
        }

        Key = Guard.Required(key, nameof(key), 200);
        DataType = Guard.Required(dataType, nameof(dataType), 50);
        Value = Guard.Optional(value, nameof(value), 4000);
        SecretReference = Guard.Optional(secretReference, nameof(secretReference), 500);
    }

    public string Key { get; private set; } = string.Empty;

    public string DataType { get; private set; } = string.Empty;

    public string? Value { get; private set; }

    public string? SecretReference { get; private set; }

    public string? Description { get; private set; }

    public bool IsReadOnly { get; private set; }

    public void UpdateValue(string dataType, string value)
    {
        if (IsReadOnly)
        {
            throw new DomainConflictException("A read-only system setting cannot be changed.");
        }

        DataType = Guard.Required(dataType, nameof(dataType), 50);
        Value = Guard.Required(value, nameof(value), 4000);
        SecretReference = null;
    }
}

public sealed class IdempotencyRecord : AuditableEntity
{
    private IdempotencyRecord()
    {
    }

    public IdempotencyRecord(
        string scope,
        string keyHash,
        string requestHash,
        DateTimeOffset expiresAt)
    {
        Scope = Guard.Required(scope, nameof(scope), 200);
        KeyHash = Guard.Required(keyHash, nameof(keyHash), 128);
        RequestHash = Guard.Required(requestHash, nameof(requestHash), 128);
        Guard.AgainstDefault(expiresAt, nameof(expiresAt));
        ExpiresAt = expiresAt;
    }

    public string Scope { get; private set; } = string.Empty;

    public string KeyHash { get; private set; } = string.Empty;

    public string RequestHash { get; private set; } = string.Empty;

    public IdempotencyRecordStatus Status { get; private set; } = IdempotencyRecordStatus.Processing;

    public int? ResponseStatusCode { get; private set; }

    public string? ResponseBody { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? LockedUntil { get; private set; }

    public void Complete(int responseStatusCode, string responseBody, DateTimeOffset completedAt)
    {
        if (Status != IdempotencyRecordStatus.Processing)
        {
            throw new DomainConflictException("Only a processing idempotency record can complete.");
        }

        if (responseStatusCode is < 100 or > 599)
        {
            throw new ArgumentOutOfRangeException(nameof(responseStatusCode));
        }

        Guard.AgainstDefault(completedAt, nameof(completedAt));
        ResponseStatusCode = responseStatusCode;
        ResponseBody = Guard.Required(responseBody, nameof(responseBody), int.MaxValue);
        CompletedAt = completedAt;
        LockedUntil = null;
        Status = IdempotencyRecordStatus.Completed;
    }

    public void Fail(int responseStatusCode, string responseBody, DateTimeOffset completedAt)
    {
        if (Status != IdempotencyRecordStatus.Processing)
        {
            throw new DomainConflictException("Only a processing idempotency record can fail.");
        }

        if (responseStatusCode is < 400 or > 599)
        {
            throw new ArgumentOutOfRangeException(nameof(responseStatusCode));
        }

        Guard.AgainstDefault(completedAt, nameof(completedAt));
        ResponseStatusCode = responseStatusCode;
        ResponseBody = Guard.Required(responseBody, nameof(responseBody), int.MaxValue);
        CompletedAt = completedAt;
        LockedUntil = null;
        Status = IdempotencyRecordStatus.Failed;
    }
}
