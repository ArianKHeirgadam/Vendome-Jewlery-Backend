using System.IdentityModel.Tokens.Jwt;
using System.Text.Encodings.Web;
using GoldInvoice.Application.Security;
using GoldInvoice.Infrastructure.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;

namespace GoldInvoice.Api.Security;

internal sealed class BearerTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> schemeOptions,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    IOptions<JwtOptions> jwtOptions,
    IAccessTokenPrincipalValidator principalValidator)
    : AuthenticationHandler<AuthenticationSchemeOptions>(schemeOptions, loggerFactory, encoder)
{
    public const string SchemeName = "Bearer";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authorization = Request.Headers[HeaderNames.Authorization].ToString();
        if (string.IsNullOrWhiteSpace(authorization))
        {
            return AuthenticateResult.NoResult();
        }

        const string prefix = "Bearer ";
        if (!authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.Fail("A valid bearer token is required.");
        }

        var encodedToken = authorization[prefix.Length..].Trim();
        if (encodedToken.Length is < 32 or > 8192)
        {
            return AuthenticateResult.Fail("A valid bearer token is required.");
        }

        try
        {
            var options = jwtOptions.Value;
            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
            var principal = handler.ValidateToken(
                encodedToken,
                CreateValidationParameters(options),
                out var validatedToken);
            if (validatedToken is not JwtSecurityToken jwt ||
                !string.Equals(jwt.Header.Alg, SecurityAlgorithms.HmacSha256, StringComparison.Ordinal) ||
                !await principalValidator.ValidateAndEnrichAsync(principal, Context.RequestAborted))
            {
                return AuthenticateResult.Fail("A valid bearer token is required.");
            }

            return AuthenticateResult.Success(
                new AuthenticationTicket(principal, Scheme.Name));
        }
        catch (SecurityTokenException)
        {
            return AuthenticateResult.Fail("A valid bearer token is required.");
        }
        catch (ArgumentException)
        {
            return AuthenticateResult.Fail("A valid bearer token is required.");
        }
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = SchemeName;
        return Task.CompletedTask;
    }

    private static TokenValidationParameters CreateValidationParameters(JwtOptions options) => new()
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
}
