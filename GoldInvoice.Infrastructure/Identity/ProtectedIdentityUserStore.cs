using GoldInvoice.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;

namespace GoldInvoice.Infrastructure.Identity;

public sealed class ProtectedIdentityUserStore(
    GoldInvoiceDbContext context,
    IdentityErrorDescriber describer,
    IDataProtectionProvider dataProtectionProvider)
    : UserStore<
        ApplicationUser,
        ApplicationRole,
        GoldInvoiceDbContext,
        Guid,
        IdentityUserClaim<Guid>,
        IdentityUserRole<Guid>,
        IdentityUserLogin<Guid>,
        IdentityUserToken<Guid>,
        IdentityRoleClaim<Guid>>(context, describer)
{
    private const string ProtectedValuePrefix = "dp:v1:";
    private const string AuthenticatorKeyTokenName = "AuthenticatorKey";
    private const string RecoveryCodesTokenName = "RecoveryCodes";

    public override Task SetTokenAsync(
        ApplicationUser user,
        string loginProvider,
        string name,
        string? value,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(loginProvider);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var storedValue = value;
        if (value is not null && IsSensitiveIdentityToken(name))
        {
            storedValue = ProtectValue(user, loginProvider, name, value);
        }

        return base.SetTokenAsync(user, loginProvider, name, storedValue, cancellationToken);
    }

    public override async Task<string?> GetTokenAsync(
        ApplicationUser user,
        string loginProvider,
        string name,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(loginProvider);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var storedValue = await base.GetTokenAsync(user, loginProvider, name, cancellationToken);
        if (storedValue is null || !IsSensitiveIdentityToken(name))
        {
            return storedValue;
        }

        if (!storedValue.StartsWith(ProtectedValuePrefix, StringComparison.Ordinal))
        {
            // Phase 3 deployments can contain plaintext Identity tokens. Migrate them on first
            // successful read without changing the value returned to Identity.
            await base.SetTokenAsync(
                user,
                loginProvider,
                name,
                ProtectValue(user, loginProvider, name, storedValue),
                cancellationToken);
            await SaveChanges(cancellationToken);
            return storedValue;
        }

        var protectedPayload = storedValue[ProtectedValuePrefix.Length..];
        return CreateProtector(user, loginProvider, name).Unprotect(protectedPayload);
    }

    private static bool IsSensitiveIdentityToken(string name) =>
        string.Equals(name, AuthenticatorKeyTokenName, StringComparison.Ordinal) ||
        string.Equals(name, RecoveryCodesTokenName, StringComparison.Ordinal);

    private IDataProtector CreateProtector(
        ApplicationUser user,
        string loginProvider,
        string tokenName) =>
        dataProtectionProvider.CreateProtector(
            "GoldInvoice.IdentityUserToken.v1",
            user.Id.ToString("N"),
            loginProvider,
            tokenName);

    private string ProtectValue(
        ApplicationUser user,
        string loginProvider,
        string tokenName,
        string value) =>
        ProtectedValuePrefix + CreateProtector(user, loginProvider, tokenName).Protect(value);
}
