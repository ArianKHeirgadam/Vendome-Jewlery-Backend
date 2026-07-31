using GoldInvoice.Application.Security;
using GoldInvoice.Domain.Security;

namespace GoldInvoice.UnitTests;

public sealed class SecurityPolicyTests
{
    [Fact]
    public void RefreshToken_CanOnlyBeRotatedOnce()
    {
        var now = new DateTimeOffset(2026, 7, 31, 20, 0, 0, TimeSpan.Zero);
        var token = new RefreshToken(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new string('A', 64),
            Guid.NewGuid(),
            now.AddDays(1));

        token.RotateTo(Guid.NewGuid(), now);

        Assert.NotNull(token.UsedAt);
        Assert.Throws<InvalidOperationException>(() =>
            token.RotateTo(Guid.NewGuid(), now.AddSeconds(1)));
    }

    [Fact]
    public void UserSession_RevocationIsIdempotentAndFinal()
    {
        var now = new DateTimeOffset(2026, 7, 31, 20, 0, 0, TimeSpan.Zero);
        var session = new UserSession(Guid.NewGuid(), now.AddDays(1), "security-stamp");

        session.Revoke(now, "Logout");
        session.Revoke(now.AddMinutes(1), "SecondReason");

        Assert.Equal(now, session.RevokedAt);
        Assert.Equal("Logout", session.RevocationReason);
        Assert.False(session.IsActiveAt(now.AddSeconds(1)));
        Assert.Throws<InvalidOperationException>(() => session.Touch(now.AddSeconds(1)));
    }

    [Fact]
    public void Admin_CannotManageAnOwner()
    {
        Assert.Throws<SecurityAccessDeniedException>(() =>
            AccountAdministrationPolicy.EnsureTargetCanBeManaged(
                Guid.NewGuid(),
                [SecurityRoles.Admin],
                Guid.NewGuid(),
                [SecurityRoles.Owner]));
    }

    [Fact]
    public void LastActiveOwner_CannotBeRemovedOrDeactivated()
    {
        Assert.Throws<SecurityAccessDeniedException>(() =>
            AccountAdministrationPolicy.EnsureOwnerContinuity(
                targetIsActiveOwner: true,
                targetWillRemainActiveOwner: false,
                activeOwnerCount: 1));
    }

    [Fact]
    public void Admin_CannotGrantAPermissionTheyDoNotHave()
    {
        Assert.Throws<SecurityAccessDeniedException>(() =>
            AccountAdministrationPolicy.EnsurePermissionsCanBeGranted(
                [SecurityRoles.Admin],
                [SecurityPermissions.ProductsRead],
                SecurityRoles.Admin,
                [SecurityPermissions.ProductsRead, SecurityPermissions.ProductsManage]));
    }

    [Fact]
    public void PermissionCatalog_HasUniqueStableNames()
    {
        Assert.Equal(
            SecurityPermissions.All.Count,
            SecurityPermissions.All.Select(permission => permission.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.All(SecurityPermissions.All, permission =>
            Assert.Matches("^[A-Za-z]+\\.[A-Za-z]+$", permission.Name));
    }
}
