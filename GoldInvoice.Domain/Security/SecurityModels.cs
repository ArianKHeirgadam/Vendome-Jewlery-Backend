using GoldInvoice.Domain.Common;

namespace GoldInvoice.Domain.Security;

public enum SecurityEventSeverity
{
    Information,
    Warning,
    Critical
}

public sealed class Permission : AuditableEntity
{
    private Permission()
    {
    }

    public Permission(string name, string displayName, string group)
    {
        Name = Guard.Required(name, nameof(name), 150);
        DisplayName = Guard.Required(displayName, nameof(displayName), 200);
        Group = Guard.Required(group, nameof(group), 100);
    }

    public string Name { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    public string Group { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public bool IsActive { get; private set; } = true;
}

public sealed class RolePermission : IAuditableEntity
{
    private RolePermission()
    {
    }

    public RolePermission(Guid roleId, Guid permissionId, Guid? grantedBy = null)
    {
        Guard.AgainstEmpty(roleId, nameof(roleId));
        Guard.AgainstEmpty(permissionId, nameof(permissionId));
        RoleId = roleId;
        PermissionId = permissionId;
        GrantedBy = grantedBy;
    }

    public Guid RoleId { get; private set; }

    public Guid PermissionId { get; private set; }

    public DateTimeOffset GrantedAt { get; private set; }

    public Guid? GrantedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    public byte[] RowVersion { get; private set; } = [];
}

public sealed class RefreshToken : AuditableEntity
{
    private RefreshToken()
    {
    }

    public RefreshToken(
        Guid userId,
        Guid sessionId,
        string tokenHash,
        Guid familyId,
        DateTimeOffset expiresAt,
        Guid? parentTokenId = null)
    {
        Guard.AgainstEmpty(userId, nameof(userId));
        Guard.AgainstEmpty(sessionId, nameof(sessionId));
        Guard.AgainstEmpty(familyId, nameof(familyId));
        Guard.AgainstDefault(expiresAt, nameof(expiresAt));
        UserId = userId;
        SessionId = sessionId;
        TokenHash = Guard.Required(tokenHash, nameof(tokenHash), 128);
        FamilyId = familyId;
        ExpiresAt = expiresAt;
        ParentTokenId = parentTokenId;
    }

    public Guid UserId { get; private set; }

    public Guid SessionId { get; private set; }

    public string TokenHash { get; private set; } = string.Empty;

    public Guid FamilyId { get; private set; }

    public Guid? ParentTokenId { get; private set; }

    public Guid? ReplacedByTokenId { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? UsedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public string? RevocationReason { get; private set; }

    public bool IsActiveAt(DateTimeOffset now) =>
        UsedAt is null && RevokedAt is null && ExpiresAt > now;

    public void RotateTo(Guid replacementTokenId, DateTimeOffset usedAt)
    {
        Guard.AgainstEmpty(replacementTokenId, nameof(replacementTokenId));
        Guard.AgainstDefault(usedAt, nameof(usedAt));

        if (!IsActiveAt(usedAt))
        {
            throw new InvalidOperationException("Only an active refresh token can be rotated.");
        }

        UsedAt = usedAt;
        ReplacedByTokenId = replacementTokenId;
    }

    public void Revoke(DateTimeOffset revokedAt, string reason)
    {
        Guard.AgainstDefault(revokedAt, nameof(revokedAt));

        if (RevokedAt is not null)
        {
            return;
        }

        RevokedAt = revokedAt;
        RevocationReason = Guard.Required(reason, nameof(reason), 500);
    }
}

public sealed class UserSession : AuditableEntity
{
    private UserSession()
    {
    }

    public UserSession(
        Guid userId,
        DateTimeOffset expiresAt,
        string securityStamp,
        string? ipAddress = null,
        string? userAgentHash = null,
        Guid? trustedDeviceId = null)
    {
        Guard.AgainstEmpty(userId, nameof(userId));
        Guard.AgainstDefault(expiresAt, nameof(expiresAt));
        UserId = userId;
        ExpiresAt = expiresAt;
        SecurityStamp = Guard.Required(securityStamp, nameof(securityStamp), 256);
        IpAddress = Guard.Optional(ipAddress, nameof(ipAddress), 64);
        UserAgentHash = Guard.Optional(userAgentHash, nameof(userAgentHash), 128);
        TrustedDeviceId = trustedDeviceId;
    }

    public Guid UserId { get; private set; }

    public Guid? TrustedDeviceId { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset LastSeenAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public string? RevocationReason { get; private set; }

    public string SecurityStamp { get; private set; } = string.Empty;

    public string? IpAddress { get; private set; }

    public string? UserAgentHash { get; private set; }

    public bool IsActiveAt(DateTimeOffset now) =>
        RevokedAt is null && ExpiresAt > now;

    public void Touch(DateTimeOffset seenAt)
    {
        Guard.AgainstDefault(seenAt, nameof(seenAt));

        if (!IsActiveAt(seenAt))
        {
            throw new InvalidOperationException("A revoked or expired session cannot be updated.");
        }

        LastSeenAt = seenAt;
    }

    public void Revoke(DateTimeOffset revokedAt, string reason)
    {
        Guard.AgainstDefault(revokedAt, nameof(revokedAt));

        if (RevokedAt is not null)
        {
            return;
        }

        RevokedAt = revokedAt;
        RevocationReason = Guard.Required(reason, nameof(reason), 500);
    }
}

public sealed class TrustedDevice : AuditableEntity
{
    private TrustedDevice()
    {
    }

    public TrustedDevice(Guid userId, string deviceIdentifierHash, string displayName, DateTimeOffset trustExpiresAt)
    {
        Guard.AgainstEmpty(userId, nameof(userId));
        Guard.AgainstDefault(trustExpiresAt, nameof(trustExpiresAt));
        UserId = userId;
        DeviceIdentifierHash = Guard.Required(deviceIdentifierHash, nameof(deviceIdentifierHash), 128);
        DisplayName = Guard.Required(displayName, nameof(displayName), 200);
        TrustExpiresAt = trustExpiresAt;
    }

    public Guid UserId { get; private set; }

    public string DeviceIdentifierHash { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    public DateTimeOffset TrustExpiresAt { get; private set; }

    public DateTimeOffset? LastUsedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public bool IsTrustedAt(DateTimeOffset now) =>
        RevokedAt is null && TrustExpiresAt > now;

    public void MarkUsed(DateTimeOffset usedAt)
    {
        Guard.AgainstDefault(usedAt, nameof(usedAt));

        if (!IsTrustedAt(usedAt))
        {
            throw new InvalidOperationException("A revoked or expired device cannot be trusted.");
        }

        LastUsedAt = usedAt;
    }

    public void Revoke(DateTimeOffset revokedAt)
    {
        Guard.AgainstDefault(revokedAt, nameof(revokedAt));
        RevokedAt ??= revokedAt;
    }
}

public sealed class LoginAttempt : AuditableEntity, IAppendOnlyEntity
{
    private LoginAttempt()
    {
    }

    public LoginAttempt(
        string normalizedIdentifierHash,
        bool succeeded,
        DateTimeOffset occurredAt,
        Guid? userId = null,
        string? failureReason = null,
        string? ipAddress = null,
        string? userAgentHash = null)
    {
        NormalizedIdentifierHash = Guard.Required(normalizedIdentifierHash, nameof(normalizedIdentifierHash), 128);
        Guard.AgainstDefault(occurredAt, nameof(occurredAt));
        Succeeded = succeeded;
        OccurredAt = occurredAt;
        UserId = userId;
        FailureReason = Guard.Optional(failureReason, nameof(failureReason), 200);
        IpAddress = Guard.Optional(ipAddress, nameof(ipAddress), 64);
        UserAgentHash = Guard.Optional(userAgentHash, nameof(userAgentHash), 128);
    }

    public Guid? UserId { get; private set; }

    public string NormalizedIdentifierHash { get; private set; } = string.Empty;

    public bool Succeeded { get; private set; }

    public string? FailureReason { get; private set; }

    public string? IpAddress { get; private set; }

    public string? UserAgentHash { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }
}

public sealed class SecurityEvent : AuditableEntity, IAppendOnlyEntity
{
    private SecurityEvent()
    {
    }

    public SecurityEvent(
        string eventType,
        SecurityEventSeverity severity,
        DateTimeOffset occurredAt,
        Guid? userId = null,
        Guid? sessionId = null,
        string? correlationId = null,
        string? ipAddress = null,
        string? detailsJson = null)
    {
        EventType = Guard.Required(eventType, nameof(eventType), 150);
        Guard.AgainstDefault(occurredAt, nameof(occurredAt));
        Severity = severity;
        OccurredAt = occurredAt;
        UserId = userId;
        SessionId = sessionId;
        CorrelationId = Guard.Optional(correlationId, nameof(correlationId), 128);
        IpAddress = Guard.Optional(ipAddress, nameof(ipAddress), 64);
        DetailsJson = detailsJson;
    }

    public Guid? UserId { get; private set; }

    public Guid? SessionId { get; private set; }

    public string EventType { get; private set; } = string.Empty;

    public SecurityEventSeverity Severity { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public string? CorrelationId { get; private set; }

    public string? IpAddress { get; private set; }

    public string? DetailsJson { get; private set; }
}
