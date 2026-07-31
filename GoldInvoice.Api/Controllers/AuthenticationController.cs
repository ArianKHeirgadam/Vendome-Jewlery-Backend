using GoldInvoice.Api.Security;
using GoldInvoice.Application.Security;
using GoldInvoice.Contracts.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Net.Http.Headers;

namespace GoldInvoice.Api.Controllers;

[ApiController]
[Authorize]
[RequestSizeLimit(16 * 1024)]
[Route("api/v1/auth")]
public sealed class AuthenticationController(
    IAccountAuthenticationService authenticationService) : ControllerBase
{
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicyNames.Login)]
    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        var result = await authenticationService.SignInAsync(
            new SignInCommand(
                request.Email,
                request.Password,
                request.AuthenticatorCode,
                request.RecoveryCode),
            CreateRequestContext(),
            cancellationToken);

        return Ok(new LoginResponse
        {
            Status = result.Status switch
            {
                SignInStatus.Authenticated => "authenticated",
                SignInStatus.MfaRequired => "mfa_required",
                SignInStatus.MfaEnrollmentRequired => "mfa_enrollment_required",
                _ => throw new InvalidOperationException("Unknown sign-in state.")
            },
            Tokens = result.Tokens is null ? null : MapTokens(result.Tokens),
            MfaEnrollmentToken = result.MfaEnrollmentToken
        });
    }

    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicyNames.Refresh)]
    [HttpPost("refresh")]
    public async Task<ActionResult<TokenResponse>> Refresh(
        RefreshTokenRequest request,
        CancellationToken cancellationToken)
    {
        var tokens = await authenticationService.RefreshAsync(
            request.RefreshToken,
            CreateRequestContext(),
            cancellationToken);
        return Ok(MapTokens(tokens));
    }

    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicyNames.Mfa)]
    [HttpPost("mfa/setup")]
    public async Task<ActionResult<MfaSetupResponse>> SetupMfa(
        MfaSetupRequest request,
        CancellationToken cancellationToken)
    {
        var result = await authenticationService.StartMfaEnrollmentAsync(
            request.EnrollmentToken,
            cancellationToken);
        return Ok(new MfaSetupResponse
        {
            SharedKey = result.SharedKey,
            AuthenticatorUri = result.AuthenticatorUri,
            EnrollmentToken = result.EnrollmentToken
        });
    }

    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicyNames.Mfa)]
    [HttpPost("mfa/enable")]
    public async Task<ActionResult<MfaEnableResponse>> EnableMfa(
        MfaEnableRequest request,
        CancellationToken cancellationToken)
    {
        var result = await authenticationService.CompleteMfaEnrollmentAsync(
            request.EnrollmentToken,
            request.AuthenticatorCode,
            CreateRequestContext(),
            cancellationToken);
        return Ok(new MfaEnableResponse
        {
            Tokens = MapTokens(result.Tokens),
            RecoveryCodes = result.RecoveryCodes
        });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var (userId, sessionId) = GetCurrentIdentity();
        await authenticationService.LogoutAsync(userId, sessionId, cancellationToken);
        return NoContent();
    }

    [HttpPost("logout-all")]
    public async Task<IActionResult> LogoutAll(CancellationToken cancellationToken)
    {
        var (userId, _) = GetCurrentIdentity();
        await authenticationService.LogoutAllAsync(userId, cancellationToken);
        return NoContent();
    }

    [HttpGet("sessions")]
    public async Task<ActionResult<IReadOnlyList<SessionResponse>>> GetSessions(
        CancellationToken cancellationToken)
    {
        var (userId, sessionId) = GetCurrentIdentity();
        var sessions = await authenticationService.GetSessionsAsync(
            userId,
            sessionId,
            cancellationToken);
        return Ok(sessions.Select(session => new SessionResponse
        {
            Id = session.Id,
            CreatedAt = session.CreatedAt,
            LastSeenAt = session.LastSeenAt,
            ExpiresAt = session.ExpiresAt,
            RevokedAt = session.RevokedAt,
            IpAddress = session.IpAddress,
            IsCurrent = session.IsCurrent
        }).ToArray());
    }

    [HttpDelete("sessions/{sessionId:guid}")]
    public async Task<IActionResult> RevokeSession(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var (userId, _) = GetCurrentIdentity();
        await authenticationService.RevokeSessionAsync(userId, sessionId, cancellationToken);
        return NoContent();
    }

    [HttpGet("me")]
    public async Task<ActionResult<CurrentUserResponse>> GetCurrentUser(
        CancellationToken cancellationToken)
    {
        var (userId, sessionId) = GetCurrentIdentity();
        var currentUser = await authenticationService.GetCurrentUserAsync(
            userId,
            sessionId,
            cancellationToken);
        return Ok(new CurrentUserResponse
        {
            Id = currentUser.Id,
            Email = currentUser.Email,
            DisplayName = currentUser.DisplayName,
            EmailConfirmed = currentUser.EmailConfirmed,
            MfaEnabled = currentUser.MfaEnabled,
            Roles = currentUser.Roles,
            Permissions = currentUser.Permissions,
            SessionId = currentUser.SessionId
        });
    }

    private RequestSecurityContext CreateRequestContext() => new(
        HttpContext.Connection.RemoteIpAddress?.ToString(),
        Request.Headers[HeaderNames.UserAgent].ToString(),
        HttpContext.TraceIdentifier);

    private (Guid UserId, Guid SessionId) GetCurrentIdentity()
    {
        if (!Guid.TryParse(User.FindFirst(SecurityClaimNames.Subject)?.Value, out var userId) ||
            !Guid.TryParse(User.FindFirst(SecurityClaimNames.SessionId)?.Value, out var sessionId))
        {
            throw new AuthenticationRejectedException();
        }

        return (userId, sessionId);
    }

    private static TokenResponse MapTokens(TokenPair tokens) => new()
    {
        TokenType = TokenResponse.BearerTokenType,
        AccessToken = tokens.AccessToken,
        AccessTokenExpiresAt = tokens.AccessTokenExpiresAt,
        RefreshToken = tokens.RefreshToken,
        RefreshTokenExpiresAt = tokens.RefreshTokenExpiresAt,
        SessionId = tokens.SessionId
    };
}
