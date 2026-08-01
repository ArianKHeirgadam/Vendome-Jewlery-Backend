using System.Security.Claims;
using GoldInvoice.Application.Security;

namespace GoldInvoice.Api.Security;

internal static class CurrentUserExtensions
{
    public static Guid GetRequiredUserId(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        if (!Guid.TryParse(principal.FindFirst(SecurityClaimNames.Subject)?.Value, out var userId))
        {
            throw new AuthenticationRejectedException();
        }

        return userId;
    }

    public static bool HasPermission(this ClaimsPrincipal principal, string permission)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return principal.HasClaim(SecurityClaimNames.Permission, permission);
    }
}
