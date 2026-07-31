using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using GoldInvoice.Application.Security;
using GoldInvoice.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace GoldInvoice.Infrastructure.Security;

internal sealed record IssuedAccessToken(string Value, DateTimeOffset ExpiresAt);

internal sealed record GeneratedRefreshToken(string Value, string Hash);

internal interface ISecurityTokenService
{
    IssuedAccessToken CreateAccessToken(
        Guid userId,
        Guid sessionId,
        string securityStamp,
        bool mfaAuthenticated);

    string CreateMfaEnrollmentToken(Guid userId, string securityStamp);

    bool TryValidateMfaEnrollmentToken(
        string token,
        out Guid userId,
        out string securityStampHash);

    GeneratedRefreshToken CreateRefreshToken();

    string HashOpaqueToken(string token);
}

internal sealed class SecurityTokenService(
    IOptions<JwtOptions> jwtOptions,
    TimeProvider timeProvider) : ISecurityTokenService
{
    private readonly JwtOptions options = jwtOptions.Value;

    public IssuedAccessToken CreateAccessToken(
        Guid userId,
        Guid sessionId,
        string securityStamp,
        bool mfaAuthenticated)
    {
        var now = timeProvider.GetUtcNow();
        var expiresAt = now.AddMinutes(options.AccessTokenLifetimeMinutes);
        var claims = CreateBaseClaims(
            userId,
            SecurityTokenUses.Access,
            securityStamp,
            now);

        claims.Add(new Claim(SecurityClaimNames.SessionId, sessionId.ToString("D")));
        claims.Add(new Claim(SecurityClaimNames.AuthenticationMethod, AuthenticationMethods.Password));
        if (mfaAuthenticated)
        {
            claims.Add(new Claim(SecurityClaimNames.AuthenticationMethod, AuthenticationMethods.Mfa));
        }

        return new IssuedAccessToken(CreateToken(claims, now, expiresAt), expiresAt);
    }

    public string CreateMfaEnrollmentToken(Guid userId, string securityStamp)
    {
        var now = timeProvider.GetUtcNow();
        var expiresAt = now.AddMinutes(options.MfaEnrollmentTokenLifetimeMinutes);
        var claims = CreateBaseClaims(
            userId,
            SecurityTokenUses.MfaEnrollment,
            securityStamp,
            now);
        claims.Add(new Claim(SecurityClaimNames.AuthenticationMethod, AuthenticationMethods.Password));

        return CreateToken(claims, now, expiresAt);
    }

    public bool TryValidateMfaEnrollmentToken(
        string token,
        out Guid userId,
        out string securityStampHash)
    {
        userId = Guid.Empty;
        securityStampHash = string.Empty;

        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        try
        {
            var handler = CreateHandler();
            var principal = handler.ValidateToken(token, CreateValidationParameters(), out var validatedToken);
            if (validatedToken is not JwtSecurityToken jwt ||
                !string.Equals(jwt.Header.Alg, SecurityAlgorithms.HmacSha256, StringComparison.Ordinal) ||
                !string.Equals(
                    principal.FindFirstValue(SecurityClaimNames.TokenUse),
                    SecurityTokenUses.MfaEnrollment,
                    StringComparison.Ordinal) ||
                !Guid.TryParse(principal.FindFirstValue(SecurityClaimNames.Subject), out userId))
            {
                userId = Guid.Empty;
                return false;
            }

            securityStampHash = principal.FindFirstValue(SecurityClaimNames.SecurityStampHash) ?? string.Empty;
            return securityStampHash.Length == 64;
        }
        catch (SecurityTokenException)
        {
            userId = Guid.Empty;
            securityStampHash = string.Empty;
            return false;
        }
        catch (ArgumentException)
        {
            userId = Guid.Empty;
            securityStampHash = string.Empty;
            return false;
        }
    }

    public GeneratedRefreshToken CreateRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        var value = Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return new GeneratedRefreshToken(value, HashOpaqueToken(value));
    }

    public string HashOpaqueToken(string token) => SecurityHashing.Sha256(token);

    private static List<Claim> CreateBaseClaims(
        Guid userId,
        string tokenUse,
        string securityStamp,
        DateTimeOffset issuedAt) =>
    [
        new(SecurityClaimNames.Subject, userId.ToString("D")),
        new(SecurityClaimNames.TokenId, Guid.NewGuid().ToString("D")),
        new(SecurityClaimNames.TokenUse, tokenUse),
        new(SecurityClaimNames.SecurityStampHash, SecurityHashing.Sha256(securityStamp)),
        new(JwtRegisteredClaimNames.Iat, issuedAt.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
    ];

    private string CreateToken(
        IEnumerable<Claim> claims,
        DateTimeOffset notBefore,
        DateTimeOffset expiresAt)
    {
        var key = new SymmetricSecurityKey(Convert.FromBase64String(options.SigningKey));
        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims: claims,
            notBefore: notBefore.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return CreateHandler().WriteToken(token);
    }

    private TokenValidationParameters CreateValidationParameters() => new()
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Convert.FromBase64String(options.SigningKey)),
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        ValidateIssuer = true,
        ValidIssuer = options.Issuer,
        ValidateAudience = true,
        ValidAudience = options.Audience,
        ValidateLifetime = true,
        RequireExpirationTime = true,
        RequireSignedTokens = true,
        ClockSkew = TimeSpan.FromSeconds(options.ClockSkewSeconds),
        NameClaimType = SecurityClaimNames.Subject,
        RoleClaimType = SecurityClaimNames.Role
    };

    private static JwtSecurityTokenHandler CreateHandler() => new()
    {
        MapInboundClaims = false
    };
}
