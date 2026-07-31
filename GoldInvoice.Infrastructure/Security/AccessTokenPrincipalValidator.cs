using System.Security.Claims;
using GoldInvoice.Application.Security;
using GoldInvoice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoldInvoice.Infrastructure.Security;

internal sealed class AccessTokenPrincipalValidator(
    GoldInvoiceDbContext dbContext,
    TimeProvider timeProvider) : IAccessTokenPrincipalValidator
{
    public async Task<bool> ValidateAndEnrichAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(principal);

        if (!string.Equals(
                FindClaim(principal, SecurityClaimNames.TokenUse),
                SecurityTokenUses.Access,
                StringComparison.Ordinal) ||
            !Guid.TryParse(FindClaim(principal, SecurityClaimNames.Subject), out var userId) ||
            !Guid.TryParse(FindClaim(principal, SecurityClaimNames.SessionId), out var sessionId) ||
            string.IsNullOrWhiteSpace(FindClaim(principal, SecurityClaimNames.TokenId)))
        {
            return false;
        }

        var stampHash = FindClaim(principal, SecurityClaimNames.SecurityStampHash);
        if (stampHash is null || stampHash.Length != 64)
        {
            return false;
        }

        var now = timeProvider.GetUtcNow();
        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);
        var session = await dbContext.UserSessions
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate =>
                candidate.Id == sessionId && candidate.UserId == userId,
                cancellationToken);

        if (user is null || session is null || !user.IsActive || !user.EmailConfirmed ||
            !session.IsActiveAt(now) || string.IsNullOrWhiteSpace(user.SecurityStamp) ||
            !SecurityHashing.FixedTimeEquals(session.SecurityStamp, user.SecurityStamp) ||
            !SecurityHashing.FixedTimeEquals(stampHash, SecurityHashing.Sha256(user.SecurityStamp)))
        {
            return false;
        }

        var access = await SecurityAccessQueries.ResolveAsync(dbContext, userId, cancellationToken);
        var privileged = access.Roles.Contains(SecurityRoles.Owner, StringComparer.Ordinal) ||
            access.Roles.Contains(SecurityRoles.Admin, StringComparer.Ordinal);
        var mfaAuthenticated = principal.Claims.Any(claim =>
            claim.Type == SecurityClaimNames.AuthenticationMethod &&
            claim.Value == AuthenticationMethods.Mfa);
        if (privileged && (!user.TwoFactorEnabled || !mfaAuthenticated))
        {
            return false;
        }

        if (principal.Identity is not ClaimsIdentity identity)
        {
            return false;
        }

        identity.AddClaim(new Claim(SecurityClaimNames.DisplayName, user.DisplayName));
        foreach (var role in access.Roles)
        {
            identity.AddClaim(new Claim(SecurityClaimNames.Role, role));
        }

        foreach (var permission in access.Permissions)
        {
            identity.AddClaim(new Claim(SecurityClaimNames.Permission, permission));
        }

        return true;
    }

    private static string? FindClaim(ClaimsPrincipal principal, string claimType) =>
        principal.FindFirst(claimType)?.Value;
}
