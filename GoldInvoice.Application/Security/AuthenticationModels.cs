using System.Security.Claims;

namespace GoldInvoice.Application.Security;

public sealed record RequestSecurityContext(
    string? IpAddress,
    string? UserAgent,
    string? CorrelationId);

public sealed record SignInCommand(
    string Email,
    string Password,
    string? AuthenticatorCode,
    string? RecoveryCode);

public enum SignInStatus
{
    Authenticated,
    MfaRequired,
    MfaEnrollmentRequired
}

public sealed record TokenPair(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    Guid SessionId);

public sealed record SignInOutcome(
    SignInStatus Status,
    TokenPair? Tokens = null,
    string? MfaEnrollmentToken = null);

public sealed record MfaSetupResult(
    string SharedKey,
    string AuthenticatorUri,
    string EnrollmentToken);

public sealed record MfaEnableResult(TokenPair Tokens, IReadOnlyList<string> RecoveryCodes);

public sealed record SessionInfo(
    Guid Id,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? RevokedAt,
    string? IpAddress,
    bool IsCurrent);

public sealed record CurrentUserInfo(
    Guid Id,
    string Email,
    string DisplayName,
    bool EmailConfirmed,
    bool MfaEnabled,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions,
    Guid SessionId);

public interface IAccountAuthenticationService
{
    Task<SignInOutcome> SignInAsync(
        SignInCommand command,
        RequestSecurityContext requestContext,
        CancellationToken cancellationToken);

    Task<TokenPair> RefreshAsync(
        string refreshToken,
        RequestSecurityContext requestContext,
        CancellationToken cancellationToken);

    Task<MfaSetupResult> StartMfaEnrollmentAsync(
        string enrollmentToken,
        CancellationToken cancellationToken);

    Task<MfaEnableResult> CompleteMfaEnrollmentAsync(
        string enrollmentToken,
        string authenticatorCode,
        RequestSecurityContext requestContext,
        CancellationToken cancellationToken);

    Task LogoutAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken);

    Task LogoutAllAsync(Guid userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<SessionInfo>> GetSessionsAsync(
        Guid userId,
        Guid currentSessionId,
        CancellationToken cancellationToken);

    Task RevokeSessionAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken);

    Task<CurrentUserInfo> GetCurrentUserAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken);
}

public interface IAccessTokenPrincipalValidator
{
    Task<bool> ValidateAndEnrichAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken);
}
