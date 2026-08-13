using GoldInvoice.Application.Security;
using GoldInvoice.Domain.Security;
using GoldInvoice.Infrastructure.Configuration;
using GoldInvoice.Infrastructure.Identity;
using GoldInvoice.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace GoldInvoice.Infrastructure.Security;

internal sealed class AccountAuthenticationService : IAccountAuthenticationService
{
    private const string InvalidCredentialsReason = "InvalidCredentials";
    private const string InvalidMfaReason = "InvalidMfa";
    private const string RefreshReuseReason = "RefreshTokenReuse";
    private readonly UserManager<ApplicationUser> userManager;
    private readonly GoldInvoiceDbContext dbContext;
    private readonly ISecurityTokenService tokenService;
    private readonly IDummyPasswordVerifier dummyPasswordVerifier;
    private readonly IdentitySecurityOptions securityOptions;
    private readonly TimeProvider timeProvider;

    public AccountAuthenticationService(
        UserManager<ApplicationUser> userManager,
        GoldInvoiceDbContext dbContext,
        ISecurityTokenService tokenService,
        IDummyPasswordVerifier dummyPasswordVerifier,
        IOptions<IdentitySecurityOptions> securityOptions,
        TimeProvider timeProvider)
    {
        this.userManager = userManager;
        this.dbContext = dbContext;
        this.tokenService = tokenService;
        this.dummyPasswordVerifier = dummyPasswordVerifier;
        this.securityOptions = securityOptions.Value;
        this.timeProvider = timeProvider;
    }

    public async Task<SignInOutcome> SignInAsync(
        SignInCommand command,
        RequestSecurityContext requestContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var identifier = command.Identifier.Trim();
        var isPhoneNumber = ContactIdentifierNormalizer.TryNormalizePhoneNumber(
            identifier,
            out var normalizedPhoneNumber);
        var normalizedIdentifier = isPhoneNumber
            ? userManager.NormalizeName(normalizedPhoneNumber) ?? normalizedPhoneNumber
            : userManager.NormalizeEmail(identifier) ?? identifier.ToUpperInvariant();
        var identifierHash = SecurityHashing.Sha256(normalizedIdentifier);
        var user = isPhoneNumber
            ? await userManager.FindByNameAsync(normalizedPhoneNumber)
            : await userManager.FindByEmailAsync(identifier);

        if (user is null)
        {
            dummyPasswordVerifier.Verify(command.Password);
            await RecordLoginAttemptAsync(
                identifierHash,
                succeeded: false,
                userId: null,
                InvalidCredentialsReason,
                requestContext,
                cancellationToken);
            throw new AuthenticationRejectedException();
        }

        if (await userManager.IsLockedOutAsync(user))
        {
            dummyPasswordVerifier.Verify(command.Password);
            await RecordLoginAttemptAsync(
                identifierHash,
                succeeded: false,
                user.Id,
                InvalidCredentialsReason,
                requestContext,
                cancellationToken);
            throw new AuthenticationRejectedException();
        }

        if (!await userManager.CheckPasswordAsync(user, command.Password))
        {
            ThrowIfIdentityFailed(
                await userManager.AccessFailedAsync(user),
                "The failed sign-in could not be recorded.");
            await RecordLoginAttemptAsync(
                identifierHash,
                succeeded: false,
                user.Id,
                InvalidCredentialsReason,
                requestContext,
                cancellationToken);
            throw new AuthenticationRejectedException();
        }

        if (!user.IsActive || !IsContactConfirmed(user))
        {
            await RecordLoginAttemptAsync(
                identifierHash,
                succeeded: false,
                user.Id,
                InvalidCredentialsReason,
                requestContext,
                cancellationToken);
            throw new AuthenticationRejectedException();
        }

        var access = await SecurityAccessQueries.ResolveAsync(dbContext, user.Id, cancellationToken);
        var mfaRequired = user.MfaRequired || user.TwoFactorEnabled ||
            access.Roles.Contains(SecurityRoles.Owner, StringComparer.Ordinal) ||
            access.Roles.Contains(SecurityRoles.Admin, StringComparer.Ordinal);

        if (mfaRequired && !user.TwoFactorEnabled)
        {
            await RecordLoginAttemptAsync(
                identifierHash,
                succeeded: false,
                user.Id,
                "MfaEnrollmentRequired",
                requestContext,
                cancellationToken);
            var stamp = await GetSecurityStampAsync(user);
            return new SignInOutcome(
                SignInStatus.MfaEnrollmentRequired,
                MfaEnrollmentToken: tokenService.CreateMfaEnrollmentToken(user.Id, stamp));
        }

        if (mfaRequired && string.IsNullOrWhiteSpace(command.AuthenticatorCode) &&
            string.IsNullOrWhiteSpace(command.RecoveryCode))
        {
            return new SignInOutcome(SignInStatus.MfaRequired);
        }

        if (mfaRequired && !await VerifySecondFactorAsync(user, command))
        {
            ThrowIfIdentityFailed(
                await userManager.AccessFailedAsync(user),
                "The failed MFA attempt could not be recorded.");
            await RecordLoginAttemptAsync(
                identifierHash,
                succeeded: false,
                user.Id,
                InvalidMfaReason,
                requestContext,
                cancellationToken);
            throw new AuthenticationRejectedException();
        }

        if (user.AccessFailedCount > 0)
        {
            ThrowIfIdentityFailed(
                await userManager.ResetAccessFailedCountAsync(user),
                "The sign-in state could not be reset.");
        }

        dbContext.LoginAttempts.Add(CreateLoginAttempt(
            identifierHash,
            succeeded: true,
            user.Id,
            failureReason: null,
            requestContext));
        var tokens = await CreateSessionAndTokensAsync(
            user,
            mfaAuthenticated: mfaRequired,
            requestContext,
            cancellationToken);

        return new SignInOutcome(SignInStatus.Authenticated, tokens);
    }

    public async Task<TokenPair> RefreshAsync(
        string refreshToken,
        RequestSecurityContext requestContext,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken) || refreshToken.Length > 512)
        {
            throw new AuthenticationRejectedException();
        }

        var tokenHash = tokenService.HashOpaqueToken(refreshToken);
        await using var transaction = await BeginTransactionAsync(cancellationToken);
        try
        {
            var currentToken = await dbContext.RefreshTokens
                .SingleOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);
            if (currentToken is null)
            {
                throw new AuthenticationRejectedException();
            }

            var session = await dbContext.UserSessions
                .SingleOrDefaultAsync(candidate => candidate.Id == currentToken.SessionId, cancellationToken);
            var user = await dbContext.Users
                .SingleOrDefaultAsync(candidate => candidate.Id == currentToken.UserId, cancellationToken);
            var now = timeProvider.GetUtcNow();

            if (currentToken.UsedAt is not null || currentToken.ReplacedByTokenId is not null)
            {
                await RevokeFamilyAndSessionAsync(
                    currentToken,
                    session,
                    now,
                    RefreshReuseReason,
                    requestContext,
                    cancellationToken);
                await CommitAsync(transaction, cancellationToken);
                throw new AuthenticationRejectedException();
            }

            if (user is null || session is null || !user.IsActive || !IsContactConfirmed(user) ||
                !currentToken.IsActiveAt(now) || !session.IsActiveAt(now) ||
                string.IsNullOrWhiteSpace(user.SecurityStamp) ||
                !SecurityHashing.FixedTimeEquals(session.SecurityStamp, user.SecurityStamp))
            {
                if (session is not null)
                {
                    await RevokeFamilyAndSessionAsync(
                        currentToken,
                        session,
                        now,
                        "RefreshRejected",
                        requestContext,
                        cancellationToken);
                    await CommitAsync(transaction, cancellationToken);
                }

                throw new AuthenticationRejectedException();
            }

            var generatedRefreshToken = tokenService.CreateRefreshToken();
            var refreshExpiresAt = Minimum(
                now.AddDays(securityOptions.RefreshTokenLifetimeDays),
                session.ExpiresAt);
            if (refreshExpiresAt <= now)
            {
                await RevokeFamilyAndSessionAsync(
                    currentToken,
                    session,
                    now,
                    "SessionExpired",
                    requestContext,
                    cancellationToken);
                await CommitAsync(transaction, cancellationToken);
                throw new AuthenticationRejectedException();
            }

            var replacement = new RefreshToken(
                user.Id,
                session.Id,
                generatedRefreshToken.Hash,
                currentToken.FamilyId,
                refreshExpiresAt,
                currentToken.Id);
            currentToken.RotateTo(replacement.Id, now);
            session.Touch(now);
            dbContext.RefreshTokens.Add(replacement);
            await dbContext.SaveChangesAsync(cancellationToken);
            await CommitAsync(transaction, cancellationToken);

            var accessToken = tokenService.CreateAccessToken(
                user.Id,
                session.Id,
                user.SecurityStamp,
                mfaAuthenticated: user.TwoFactorEnabled);
            return new TokenPair(
                accessToken.Value,
                accessToken.ExpiresAt,
                generatedRefreshToken.Value,
                refreshExpiresAt,
                session.Id);
        }
        catch (DbUpdateConcurrencyException)
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            dbContext.ChangeTracker.Clear();
            await RevokeReusedTokenAsync(tokenHash, requestContext, cancellationToken);
            throw new AuthenticationRejectedException();
        }
    }

    public async Task<MfaSetupResult> StartMfaEnrollmentAsync(
        string enrollmentToken,
        CancellationToken cancellationToken)
    {
        var user = await GetMfaEnrollmentUserAsync(enrollmentToken, cancellationToken);
        var key = await userManager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrWhiteSpace(key))
        {
            ThrowIfIdentityFailed(
                await userManager.ResetAuthenticatorKeyAsync(user),
                "MFA enrollment could not be initialized.");
            key = await userManager.GetAuthenticatorKeyAsync(user);
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException("MFA enrollment did not produce an authenticator key.");
        }

        var accountName = user.Email ?? user.UserName ?? user.Id.ToString("D");
        var issuer = securityOptions.AuthenticatorIssuer;
        var authenticatorUri = $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(accountName)}" +
            $"?secret={Uri.EscapeDataString(key)}&issuer={Uri.EscapeDataString(issuer)}&digits=6";

        var refreshedStamp = await GetSecurityStampAsync(user);
        return new MfaSetupResult(
            key,
            authenticatorUri,
            tokenService.CreateMfaEnrollmentToken(user.Id, refreshedStamp));
    }

    public async Task<MfaEnableResult> CompleteMfaEnrollmentAsync(
        string enrollmentToken,
        string authenticatorCode,
        RequestSecurityContext requestContext,
        CancellationToken cancellationToken)
    {
        var user = await GetMfaEnrollmentUserAsync(enrollmentToken, cancellationToken);
        var normalizedCode = NormalizeAuthenticatorCode(authenticatorCode);
        var valid = await userManager.VerifyTwoFactorTokenAsync(
            user,
            TokenOptions.DefaultAuthenticatorProvider,
            normalizedCode);
        if (!valid)
        {
            ThrowIfIdentityFailed(
                await userManager.AccessFailedAsync(user),
                "The failed MFA attempt could not be recorded.");
            throw new AuthenticationRejectedException();
        }

        await using var transaction = await BeginTransactionAsync(cancellationToken);
        ThrowIfIdentityFailed(
            await userManager.SetTwoFactorEnabledAsync(user, true),
            "MFA could not be enabled.");
        var recoveryCodes = await userManager.GenerateNewTwoFactorRecoveryCodesAsync(
            user,
            securityOptions.RecoveryCodeCount);
        ThrowIfIdentityFailed(
            await userManager.UpdateSecurityStampAsync(user),
            "The account security stamp could not be updated.");

        dbContext.SecurityEvents.Add(new SecurityEvent(
            "MfaEnabled",
            SecurityEventSeverity.Information,
            timeProvider.GetUtcNow(),
            user.Id,
            correlationId: requestContext.CorrelationId,
            ipAddress: NormalizeIpAddress(requestContext.IpAddress)));

        var tokens = await CreateSessionAndTokensAsync(
            user,
            mfaAuthenticated: true,
            requestContext,
            cancellationToken,
            transaction);
        await CommitAsync(transaction, cancellationToken);
        return new MfaEnableResult(tokens, recoveryCodes?.ToArray() ?? []);
    }

    public async Task LogoutAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken)
    {
        var session = await GetOwnedSessionAsync(userId, sessionId, cancellationToken);
        await RevokeSessionCoreAsync(session, "Logout", cancellationToken);
    }

    public async Task LogoutAllAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var transaction = await BeginTransactionAsync(cancellationToken);
        var user = await userManager.FindByIdAsync(userId.ToString("D")) ??
            throw new SecurityResourceNotFoundException();
        var now = timeProvider.GetUtcNow();
        var sessions = await dbContext.UserSessions
            .Where(session => session.UserId == userId && session.RevokedAt == null)
            .ToListAsync(cancellationToken);
        var sessionIds = sessions.Select(session => session.Id).ToArray();
        var tokens = await dbContext.RefreshTokens
            .Where(token => sessionIds.Contains(token.SessionId) && token.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var session in sessions)
        {
            session.Revoke(now, "LogoutAll");
        }

        foreach (var token in tokens)
        {
            token.Revoke(now, "LogoutAll");
        }

        ThrowIfIdentityFailed(
            await userManager.UpdateSecurityStampAsync(user),
            "The account security stamp could not be updated.");
        dbContext.SecurityEvents.Add(new SecurityEvent(
            "AllSessionsRevoked",
            SecurityEventSeverity.Information,
            now,
            userId));
        await dbContext.SaveChangesAsync(cancellationToken);
        await CommitAsync(transaction, cancellationToken);
    }

    public async Task<IReadOnlyList<SessionInfo>> GetSessionsAsync(
        Guid userId,
        Guid currentSessionId,
        CancellationToken cancellationToken) =>
        await dbContext.UserSessions
            .AsNoTracking()
            .Where(session => session.UserId == userId)
            .OrderByDescending(session => session.LastSeenAt)
            .Take(100)
            .Select(session => new SessionInfo(
                session.Id,
                session.CreatedAt,
                session.LastSeenAt,
                session.ExpiresAt,
                session.RevokedAt,
                session.IpAddress,
                session.Id == currentSessionId))
            .ToListAsync(cancellationToken);

    public async Task RevokeSessionAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var session = await GetOwnedSessionAsync(userId, sessionId, cancellationToken);
        await RevokeSessionCoreAsync(session, "UserRevoked", cancellationToken);
    }

    public async Task<CurrentUserInfo> GetCurrentUserAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken) ??
            throw new SecurityResourceNotFoundException();
        var now = timeProvider.GetUtcNow();
        var sessionIsActive = await dbContext.UserSessions
            .AsNoTracking()
            .AnyAsync(session =>
                session.Id == sessionId &&
                session.UserId == userId &&
                session.RevokedAt == null &&
                session.ExpiresAt > now,
                cancellationToken);
        if (!sessionIsActive || !user.IsActive)
        {
            throw new AuthenticationRejectedException();
        }

        var access = await SecurityAccessQueries.ResolveAsync(dbContext, userId, cancellationToken);
        return new CurrentUserInfo(
            user.Id,
            user.Email ?? string.Empty,
            user.DisplayName,
            IsContactConfirmed(user),
            user.TwoFactorEnabled,
            access.Roles,
            access.Permissions,
            sessionId);
    }

    private async Task<TokenPair> CreateSessionAndTokensAsync(
        ApplicationUser user,
        bool mfaAuthenticated,
        RequestSecurityContext requestContext,
        CancellationToken cancellationToken,
        IDbContextTransaction? ambientTransaction = null)
    {
        var now = timeProvider.GetUtcNow();
        var securityStamp = await GetSecurityStampAsync(user);
        var sessionExpiresAt = now.AddDays(securityOptions.SessionLifetimeDays);
        var refreshExpiresAt = Minimum(
            now.AddDays(securityOptions.RefreshTokenLifetimeDays),
            sessionExpiresAt);
        var generatedRefreshToken = tokenService.CreateRefreshToken();
        var session = new UserSession(
            user.Id,
            sessionExpiresAt,
            securityStamp,
            NormalizeIpAddress(requestContext.IpAddress),
            HashOptional(requestContext.UserAgent));
        var refreshToken = new RefreshToken(
            user.Id,
            session.Id,
            generatedRefreshToken.Hash,
            Guid.NewGuid(),
            refreshExpiresAt);

        await using var ownedTransaction = ambientTransaction is null
            ? await BeginTransactionAsync(cancellationToken)
            : null;
        dbContext.UserSessions.Add(session);
        dbContext.RefreshTokens.Add(refreshToken);
        dbContext.SecurityEvents.Add(new SecurityEvent(
            "SessionCreated",
            SecurityEventSeverity.Information,
            now,
            user.Id,
            session.Id,
            requestContext.CorrelationId,
            NormalizeIpAddress(requestContext.IpAddress)));
        await dbContext.SaveChangesAsync(cancellationToken);
        await CommitAsync(ownedTransaction, cancellationToken);

        var accessToken = tokenService.CreateAccessToken(
            user.Id,
            session.Id,
            securityStamp,
            mfaAuthenticated);
        return new TokenPair(
            accessToken.Value,
            accessToken.ExpiresAt,
            generatedRefreshToken.Value,
            refreshExpiresAt,
            session.Id);
    }

    private async Task<ApplicationUser> GetMfaEnrollmentUserAsync(
        string enrollmentToken,
        CancellationToken cancellationToken)
    {
        if (!tokenService.TryValidateMfaEnrollmentToken(
                enrollmentToken,
                out var userId,
                out var stampHash))
        {
            throw new AuthenticationRejectedException();
        }

        cancellationToken.ThrowIfCancellationRequested();
        var user = await userManager.FindByIdAsync(userId.ToString("D"));
        if (user is null || !user.IsActive || !IsContactConfirmed(user) || user.TwoFactorEnabled ||
            string.IsNullOrWhiteSpace(user.SecurityStamp) ||
            !SecurityHashing.FixedTimeEquals(stampHash, SecurityHashing.Sha256(user.SecurityStamp)))
        {
            throw new AuthenticationRejectedException();
        }

        var access = await SecurityAccessQueries.ResolveAsync(dbContext, user.Id, cancellationToken);
        var mustEnroll = user.MfaRequired ||
            access.Roles.Contains(SecurityRoles.Owner, StringComparer.Ordinal) ||
            access.Roles.Contains(SecurityRoles.Admin, StringComparer.Ordinal);
        if (!mustEnroll)
        {
            throw new AuthenticationRejectedException();
        }

        return user;
    }

    private static bool IsContactConfirmed(ApplicationUser user) =>
        (!string.IsNullOrWhiteSpace(user.Email) && user.EmailConfirmed) ||
        (!string.IsNullOrWhiteSpace(user.PhoneNumber) && user.PhoneNumberConfirmed);

    private async Task<bool> VerifySecondFactorAsync(
        ApplicationUser user,
        SignInCommand command)
    {
        if (!string.IsNullOrWhiteSpace(command.AuthenticatorCode) &&
            string.IsNullOrWhiteSpace(command.RecoveryCode))
        {
            return await userManager.VerifyTwoFactorTokenAsync(
                user,
                TokenOptions.DefaultAuthenticatorProvider,
                NormalizeAuthenticatorCode(command.AuthenticatorCode));
        }

        if (!string.IsNullOrWhiteSpace(command.RecoveryCode) &&
            string.IsNullOrWhiteSpace(command.AuthenticatorCode))
        {
            var result = await userManager.RedeemTwoFactorRecoveryCodeAsync(
                user,
                command.RecoveryCode.Trim());
            return result.Succeeded;
        }

        return false;
    }

    private async Task RecordLoginAttemptAsync(
        string identifierHash,
        bool succeeded,
        Guid? userId,
        string? failureReason,
        RequestSecurityContext requestContext,
        CancellationToken cancellationToken)
    {
        dbContext.LoginAttempts.Add(CreateLoginAttempt(
            identifierHash,
            succeeded,
            userId,
            failureReason,
            requestContext));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private LoginAttempt CreateLoginAttempt(
        string identifierHash,
        bool succeeded,
        Guid? userId,
        string? failureReason,
        RequestSecurityContext requestContext) => new(
            identifierHash,
            succeeded,
            timeProvider.GetUtcNow(),
            userId,
            failureReason,
            NormalizeIpAddress(requestContext.IpAddress),
            HashOptional(requestContext.UserAgent));

    private async Task<UserSession> GetOwnedSessionAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken) =>
        await dbContext.UserSessions.SingleOrDefaultAsync(
            session => session.Id == sessionId && session.UserId == userId,
            cancellationToken) ?? throw new SecurityResourceNotFoundException();

    private async Task RevokeSessionCoreAsync(
        UserSession session,
        string reason,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        session.Revoke(now, reason);
        var tokens = await dbContext.RefreshTokens
            .Where(token => token.SessionId == session.Id && token.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var token in tokens)
        {
            token.Revoke(now, reason);
        }

        dbContext.SecurityEvents.Add(new SecurityEvent(
            "SessionRevoked",
            SecurityEventSeverity.Information,
            now,
            session.UserId,
            session.Id));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task RevokeFamilyAndSessionAsync(
        RefreshToken token,
        UserSession? session,
        DateTimeOffset now,
        string reason,
        RequestSecurityContext requestContext,
        CancellationToken cancellationToken)
    {
        var familyTokens = await dbContext.RefreshTokens
            .Where(candidate => candidate.FamilyId == token.FamilyId && candidate.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var familyToken in familyTokens)
        {
            familyToken.Revoke(now, reason);
        }

        session?.Revoke(now, reason);
        dbContext.SecurityEvents.Add(new SecurityEvent(
            reason == RefreshReuseReason ? "RefreshTokenReuseDetected" : "SessionRefreshRejected",
            reason == RefreshReuseReason ? SecurityEventSeverity.Critical : SecurityEventSeverity.Warning,
            now,
            token.UserId,
            token.SessionId,
            requestContext.CorrelationId,
            NormalizeIpAddress(requestContext.IpAddress)));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task RevokeReusedTokenAsync(
        string tokenHash,
        RequestSecurityContext requestContext,
        CancellationToken cancellationToken)
    {
        await using var transaction = await BeginTransactionAsync(cancellationToken);
        var token = await dbContext.RefreshTokens
            .SingleOrDefaultAsync(candidate => candidate.TokenHash == tokenHash, cancellationToken);
        if (token is not null)
        {
            var session = await dbContext.UserSessions
                .SingleOrDefaultAsync(candidate => candidate.Id == token.SessionId, cancellationToken);
            await RevokeFamilyAndSessionAsync(
                token,
                session,
                timeProvider.GetUtcNow(),
                RefreshReuseReason,
                requestContext,
                cancellationToken);
            await CommitAsync(transaction, cancellationToken);
        }
    }

    private async Task<IDbContextTransaction?> BeginTransactionAsync(CancellationToken cancellationToken) =>
        dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

    private static async Task CommitAsync(
        IDbContextTransaction? transaction,
        CancellationToken cancellationToken)
    {
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
    }

    private async Task<string> GetSecurityStampAsync(ApplicationUser user)
    {
        var stamp = await userManager.GetSecurityStampAsync(user);
        return string.IsNullOrWhiteSpace(stamp)
            ? throw new InvalidOperationException("The account has no security stamp.")
            : stamp;
    }

    private static string NormalizeAuthenticatorCode(string value) =>
        value.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);

    private static string? HashOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : SecurityHashing.Sha256(value.Trim());

    private static string? NormalizeIpAddress(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, 64)];

    private static DateTimeOffset Minimum(DateTimeOffset left, DateTimeOffset right) =>
        left <= right ? left : right;

    private static void ThrowIfIdentityFailed(IdentityResult result, string message)
    {
        if (result.Succeeded)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }
}
