namespace GoldInvoice.Application.Security;

public static class AccountAdministrationPolicy
{
    public static void EnsureTargetCanBeManaged(
        Guid actorUserId,
        IReadOnlyCollection<string> actorRoles,
        Guid targetUserId,
        IReadOnlyCollection<string> targetRoles)
    {
        ArgumentNullException.ThrowIfNull(actorRoles);
        ArgumentNullException.ThrowIfNull(targetRoles);

        var actorIsOwner = actorRoles.Contains(SecurityRoles.Owner, StringComparer.Ordinal);
        var targetIsOwner = targetRoles.Contains(SecurityRoles.Owner, StringComparer.Ordinal);

        if (targetIsOwner && !actorIsOwner)
        {
            throw new SecurityAccessDeniedException();
        }

        if (actorUserId == targetUserId && !actorIsOwner)
        {
            throw new SecurityAccessDeniedException();
        }
    }

    public static void EnsureOwnerContinuity(
        bool targetIsActiveOwner,
        bool targetWillRemainActiveOwner,
        int activeOwnerCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(activeOwnerCount);

        if (targetIsActiveOwner && !targetWillRemainActiveOwner && activeOwnerCount <= 1)
        {
            throw new SecurityAccessDeniedException();
        }
    }

    public static void EnsurePermissionsCanBeGranted(
        IReadOnlyCollection<string> actorRoles,
        IReadOnlyCollection<string> actorPermissions,
        string targetRole,
        IReadOnlyCollection<string> requestedPermissions)
    {
        ArgumentNullException.ThrowIfNull(actorRoles);
        ArgumentNullException.ThrowIfNull(actorPermissions);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetRole);
        ArgumentNullException.ThrowIfNull(requestedPermissions);

        var actorIsOwner = actorRoles.Contains(SecurityRoles.Owner, StringComparer.Ordinal);
        if (string.Equals(targetRole, SecurityRoles.Owner, StringComparison.Ordinal) ||
            (!actorIsOwner && requestedPermissions.Except(actorPermissions, StringComparer.Ordinal).Any()))
        {
            throw new SecurityAccessDeniedException();
        }
    }
}
