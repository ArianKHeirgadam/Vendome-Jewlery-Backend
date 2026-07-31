using System.ComponentModel.DataAnnotations;

namespace GoldInvoice.Contracts.Authentication;

public sealed class LoginRequest
{
    [Required]
    [EmailAddress]
    [StringLength(320)]
    public string Email { get; init; } = string.Empty;

    [Required]
    [StringLength(256, MinimumLength = 1)]
    public string Password { get; init; } = string.Empty;

    [StringLength(16)]
    public string? AuthenticatorCode { get; init; }

    [StringLength(64)]
    public string? RecoveryCode { get; init; }
}

public sealed class RefreshTokenRequest
{
    [Required]
    [StringLength(512, MinimumLength = 32)]
    public string RefreshToken { get; init; } = string.Empty;
}

public sealed class MfaSetupRequest
{
    [Required]
    [StringLength(4096, MinimumLength = 32)]
    public string EnrollmentToken { get; init; } = string.Empty;
}

public sealed class MfaEnableRequest
{
    [Required]
    [StringLength(4096, MinimumLength = 32)]
    public string EnrollmentToken { get; init; } = string.Empty;

    [Required]
    [StringLength(16, MinimumLength = 6)]
    public string AuthenticatorCode { get; init; } = string.Empty;
}

public sealed class LoginResponse
{
    public required string Status { get; init; }

    public TokenResponse? Tokens { get; init; }

    public string? MfaEnrollmentToken { get; init; }
}

public sealed class TokenResponse
{
    public const string BearerTokenType = "Bearer";

    public required string TokenType { get; init; }

    public required string AccessToken { get; init; }

    public DateTimeOffset AccessTokenExpiresAt { get; init; }

    public required string RefreshToken { get; init; }

    public DateTimeOffset RefreshTokenExpiresAt { get; init; }

    public Guid SessionId { get; init; }
}

public sealed class MfaSetupResponse
{
    public required string SharedKey { get; init; }

    public required string AuthenticatorUri { get; init; }

    public required string EnrollmentToken { get; init; }
}

public sealed class MfaEnableResponse
{
    public required TokenResponse Tokens { get; init; }

    public required IReadOnlyList<string> RecoveryCodes { get; init; }
}

public sealed class SessionResponse
{
    public Guid Id { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset LastSeenAt { get; init; }

    public DateTimeOffset ExpiresAt { get; init; }

    public DateTimeOffset? RevokedAt { get; init; }

    public string? IpAddress { get; init; }

    public bool IsCurrent { get; init; }
}

public sealed class CurrentUserResponse
{
    public Guid Id { get; init; }

    public required string Email { get; init; }

    public required string DisplayName { get; init; }

    public bool EmailConfirmed { get; init; }

    public bool MfaEnabled { get; init; }

    public required IReadOnlyList<string> Roles { get; init; }

    public required IReadOnlyList<string> Permissions { get; init; }

    public Guid SessionId { get; init; }
}
