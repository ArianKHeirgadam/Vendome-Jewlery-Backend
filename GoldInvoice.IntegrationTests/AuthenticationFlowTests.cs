using System.IdentityModel.Tokens.Jwt;
using System.Buffers.Binary;
using System.Security.Claims;
using System.Security.Cryptography;
using GoldInvoice.Api.Security;
using GoldInvoice.Application.Security;
using GoldInvoice.Infrastructure;
using GoldInvoice.Infrastructure.Configuration;
using GoldInvoice.Infrastructure.Identity;
using GoldInvoice.Infrastructure.Persistence;
using GoldInvoice.Infrastructure.Persistence.Interceptors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace GoldInvoice.IntegrationTests;

public sealed class AuthenticationFlowTests
{
    private const string ValidPassword = "Strong-Test-Password!42";

    [Fact]
    public async Task SignIn_IssuesHashedRotatingRefreshTokenAndDetectsReuse()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var user = await CreateUserAsync(scope.ServiceProvider, SecurityRoles.Customer);
        var authentication = scope.ServiceProvider.GetRequiredService<IAccountAuthenticationService>();
        var requestContext = new RequestSecurityContext("127.0.0.1", "integration-test", "test-correlation");

        var signIn = await authentication.SignInAsync(
            new SignInCommand(user.Email!, ValidPassword, null, null),
            requestContext,
            CancellationToken.None);
        var originalTokens = Assert.IsType<TokenPair>(signIn.Tokens);
        var rotatedTokens = await authentication.RefreshAsync(
            originalTokens.RefreshToken,
            requestContext,
            CancellationToken.None);

        Assert.NotEqual(originalTokens.RefreshToken, rotatedTokens.RefreshToken);
        var dbContext = scope.ServiceProvider.GetRequiredService<GoldInvoiceDbContext>();
        var storedTokens = await dbContext.RefreshTokens.OrderBy(token => token.CreatedAt).ToListAsync();
        Assert.Equal(2, storedTokens.Count);
        Assert.DoesNotContain(storedTokens, token =>
            token.TokenHash == originalTokens.RefreshToken || token.TokenHash == rotatedTokens.RefreshToken);

        await Assert.ThrowsAsync<AuthenticationRejectedException>(() =>
            authentication.RefreshAsync(
                originalTokens.RefreshToken,
                requestContext,
                CancellationToken.None));

        var session = await dbContext.UserSessions.SingleAsync();
        Assert.NotNull(session.RevokedAt);
        Assert.All(await dbContext.RefreshTokens.ToListAsync(), token => Assert.NotNull(token.RevokedAt));
    }

    [Fact]
    public async Task SignIn_LocksAccountAfterConfiguredFailuresWithoutChangingTheResponse()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var user = await CreateUserAsync(scope.ServiceProvider, SecurityRoles.Customer);
        var authentication = scope.ServiceProvider.GetRequiredService<IAccountAuthenticationService>();
        var context = new RequestSecurityContext("127.0.0.1", "integration-test", null);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await Assert.ThrowsAsync<AuthenticationRejectedException>(() =>
                authentication.SignInAsync(
                    new SignInCommand(user.Email!, "Wrong-Test-Password!42", null, null),
                    context,
                    CancellationToken.None));
        }

        await Assert.ThrowsAsync<AuthenticationRejectedException>(() =>
            authentication.SignInAsync(
                new SignInCommand(user.Email!, ValidPassword, null, null),
                context,
                CancellationToken.None));

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var reloaded = await userManager.FindByIdAsync(user.Id.ToString("D"));
        Assert.NotNull(reloaded);
        Assert.True(await userManager.IsLockedOutAsync(reloaded));
    }

    [Fact]
    public async Task Owner_MustEnrollMfaBeforeAccessTokenIsIssued()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var owner = await CreateUserAsync(scope.ServiceProvider, SecurityRoles.Owner, requireMfa: true);
        var authentication = scope.ServiceProvider.GetRequiredService<IAccountAuthenticationService>();
        var context = new RequestSecurityContext("127.0.0.1", "integration-test", null);

        var signIn = await authentication.SignInAsync(
            new SignInCommand(owner.Email!, ValidPassword, null, null),
            context,
            CancellationToken.None);

        Assert.Equal(SignInStatus.MfaEnrollmentRequired, signIn.Status);
        Assert.Null(signIn.Tokens);
        Assert.NotNull(signIn.MfaEnrollmentToken);

        var setup = await authentication.StartMfaEnrollmentAsync(
            signIn.MfaEnrollmentToken,
            CancellationToken.None);
        Assert.False(string.IsNullOrWhiteSpace(setup.SharedKey));
        Assert.StartsWith("otpauth://totp/", setup.AuthenticatorUri, StringComparison.Ordinal);

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var code = GenerateAuthenticatorCode(setup.SharedKey, DateTimeOffset.UtcNow);
        var enabled = await authentication.CompleteMfaEnrollmentAsync(
            setup.EnrollmentToken,
            code,
            context,
            CancellationToken.None);

        Assert.NotEmpty(enabled.Tokens.AccessToken);
        Assert.Equal(10, enabled.RecoveryCodes.Count);
        Assert.True((await userManager.FindByIdAsync(owner.Id.ToString("D")))?.TwoFactorEnabled);
    }

    [Fact]
    public async Task AccessTokenValidation_LoadsCurrentRolesAndRejectsRevokedSession()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var user = await CreateUserAsync(scope.ServiceProvider, SecurityRoles.Customer);
        var authentication = scope.ServiceProvider.GetRequiredService<IAccountAuthenticationService>();
        var context = new RequestSecurityContext("127.0.0.1", "integration-test", null);
        var signIn = await authentication.SignInAsync(
            new SignInCommand(user.Email!, ValidPassword, null, null),
            context,
            CancellationToken.None);
        var tokens = Assert.IsType<TokenPair>(signIn.Tokens);
        var principal = ValidateSignature(
            tokens.AccessToken,
            scope.ServiceProvider.GetRequiredService<IOptions<JwtOptions>>().Value);
        var validator = scope.ServiceProvider.GetRequiredService<IAccessTokenPrincipalValidator>();

        Assert.True(await validator.ValidateAndEnrichAsync(principal, CancellationToken.None));
        Assert.Contains(principal.Claims, claim =>
            claim.Type == SecurityClaimNames.Role && claim.Value == SecurityRoles.Customer);

        await authentication.LogoutAsync(user.Id, tokens.SessionId, CancellationToken.None);
        var freshPrincipal = ValidateSignature(
            tokens.AccessToken,
            scope.ServiceProvider.GetRequiredService<IOptions<JwtOptions>>().Value);
        Assert.False(await validator.ValidateAndEnrichAsync(freshPrincipal, CancellationToken.None));
    }

    [Fact]
    public async Task ApiSecurity_RegistersEveryPermissionPolicy()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddProblemDetails();
        services.AddApiSecurity(CreateConfiguration());
        await using var provider = services.BuildServiceProvider();
        var policyProvider = provider.GetRequiredService<IAuthorizationPolicyProvider>();

        foreach (var permission in SecurityPermissions.All)
        {
            Assert.NotNull(await policyProvider.GetPolicyAsync(permission.Name));
        }
    }

    [Fact]
    public async Task SecurityBootstrap_IsIdempotentAndGrantsEveryPermissionToOwnerRole()
    {
        await using var provider = CreateProvider();
        var bootstrapper = Assert.Single(
            provider.GetServices<IHostedService>(),
            service => service.GetType().Name == "SecurityBootstrapHostedService");

        await bootstrapper.StartAsync(CancellationToken.None);
        await bootstrapper.StartAsync(CancellationToken.None);

        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GoldInvoiceDbContext>();
        var ownerRole = await dbContext.Roles.SingleAsync(role => role.Name == SecurityRoles.Owner);
        Assert.Equal(3, await dbContext.Roles.CountAsync());
        Assert.Equal(SecurityPermissions.All.Count, await dbContext.Permissions.CountAsync());
        Assert.Equal(
            SecurityPermissions.All.Count,
            await dbContext.RolePermissions.CountAsync(item => item.RoleId == ownerRole.Id));
    }

    [Fact]
    public async Task SecurityBootstrap_CreatesNoDefaultAccountUnlessExplicitlyConfigured()
    {
        await using var disabledProvider = CreateProvider();
        var disabledBootstrapper = GetSecurityBootstrapper(disabledProvider);
        await disabledBootstrapper.StartAsync(CancellationToken.None);
        await using (var disabledScope = disabledProvider.CreateAsyncScope())
        {
            var dbContext = disabledScope.ServiceProvider.GetRequiredService<GoldInvoiceDbContext>();
            Assert.Empty(await dbContext.Users.ToListAsync());
        }

        await using var enabledProvider = CreateProvider(bootstrapOwner: true);
        var enabledBootstrapper = GetSecurityBootstrapper(enabledProvider);
        await enabledBootstrapper.StartAsync(CancellationToken.None);
        await using var enabledScope = enabledProvider.CreateAsyncScope();
        var userManager = enabledScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var owner = await userManager.FindByEmailAsync("bootstrap-owner@example.test");

        Assert.NotNull(owner);
        Assert.True(owner.EmailConfirmed);
        Assert.True(owner.MfaRequired);
        Assert.True(await userManager.IsInRoleAsync(owner, SecurityRoles.Owner));
    }

    [Fact]
    public void SecurityInfrastructure_RejectsAWeakSigningKey()
    {
        var settings = new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "tests",
            ["Jwt:Audience"] = "tests",
            ["Jwt:SigningKey"] = Convert.ToBase64String(new byte[16])
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<GoldInvoiceDbContext>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString("N")));
        services.AddSecurityInfrastructure(configuration);
        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<JwtOptions>>().Value);
    }

    private static async Task<ApplicationUser> CreateUserAsync(
        IServiceProvider services,
        string roleName,
        bool requireMfa = false)
    {
        var roleManager = services.GetRequiredService<RoleManager<ApplicationRole>>();
        var roleResult = await roleManager.CreateAsync(new ApplicationRole(
            roleName,
            $"{roleName} test role",
            isSystem: true));
        Assert.True(roleResult.Succeeded);

        var user = new ApplicationUser($"{roleName} Test User")
        {
            Email = $"{roleName.ToLowerInvariant()}-{Guid.NewGuid():N}@example.test",
            UserName = $"{roleName.ToLowerInvariant()}-{Guid.NewGuid():N}@example.test",
            EmailConfirmed = true
        };
        user.UserName = user.Email;
        if (requireMfa)
        {
            user.RequireMfa();
        }

        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var createResult = await userManager.CreateAsync(user, ValidPassword);
        Assert.True(createResult.Succeeded, string.Join(", ", createResult.Errors.Select(error => error.Code)));
        var roleAssignment = await userManager.AddToRoleAsync(user, roleName);
        Assert.True(roleAssignment.Succeeded);
        return user;
    }

    private static ServiceProvider CreateProvider(bool bootstrapOwner = false)
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<AuditingSaveChangesInterceptor>();
        services.AddDbContext<GoldInvoiceDbContext>((provider, options) =>
            options
                .UseInMemoryDatabase(databaseName)
                .AddInterceptors(provider.GetRequiredService<AuditingSaveChangesInterceptor>()));
        services.AddSecurityInfrastructure(CreateConfiguration(bootstrapOwner));
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static IConfiguration CreateConfiguration(bool bootstrapOwner = false)
    {
        var signingKey = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
        var settings = new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "GoldInvoice.Tests",
            ["Jwt:Audience"] = "GoldInvoice.TestClients",
            ["Jwt:SigningKey"] = Convert.ToBase64String(signingKey),
            ["Jwt:AccessTokenLifetimeMinutes"] = "10",
            ["Jwt:MfaEnrollmentTokenLifetimeMinutes"] = "10",
            ["Jwt:ClockSkewSeconds"] = "0",
            ["Security:PasswordRequiredLength"] = "12",
            ["Security:MaxFailedAccessAttempts"] = "5",
            ["Security:LockoutMinutes"] = "15",
            ["Security:SessionLifetimeDays"] = "30",
            ["Security:RefreshTokenLifetimeDays"] = "14",
            ["Security:RecoveryCodeCount"] = "10",
            ["Security:AuthenticatorIssuer"] = "Vendome Tests",
            ["Security:BootstrapOwner:Enabled"] = "false"
        };
        if (bootstrapOwner)
        {
            settings["Security:BootstrapOwner:Enabled"] = "true";
            settings["Security:BootstrapOwner:Email"] = "bootstrap-owner@example.test";
            settings["Security:BootstrapOwner:Password"] = ValidPassword;
            settings["Security:BootstrapOwner:DisplayName"] = "Bootstrap Owner";
        }

        return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    }

    private static IHostedService GetSecurityBootstrapper(IServiceProvider provider) =>
        Assert.Single(
            provider.GetServices<IHostedService>(),
            service => service.GetType().Name == "SecurityBootstrapHostedService");

    private static ClaimsPrincipal ValidateSignature(string token, JwtOptions options)
    {
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        return handler.ValidateToken(token, new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Convert.FromBase64String(options.SigningKey)),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ValidateIssuer = true,
            ValidIssuer = options.Issuer,
            ValidateAudience = true,
            ValidAudience = options.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
            NameClaimType = SecurityClaimNames.Subject,
            RoleClaimType = SecurityClaimNames.Role
        }, out _);
    }

    private static string GenerateAuthenticatorCode(string base32Key, DateTimeOffset now)
    {
        var key = DecodeBase32(base32Key);
        Span<byte> counter = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64BigEndian(counter, now.ToUnixTimeSeconds() / 30);
        var hash = HMACSHA1.HashData(key, counter);
        var offset = hash[^1] & 0x0F;
        var binaryCode = ((hash[offset] & 0x7F) << 24) |
            ((hash[offset + 1] & 0xFF) << 16) |
            ((hash[offset + 2] & 0xFF) << 8) |
            (hash[offset + 3] & 0xFF);
        return (binaryCode % 1_000_000).ToString("D6");
    }

    private static byte[] DecodeBase32(string value)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        List<byte> output = [];
        output.Capacity = value.Length * 5 / 8;
        var buffer = 0;
        var bitsLeft = 0;

        foreach (var character in value.TrimEnd('=').ToUpperInvariant())
        {
            var index = alphabet.IndexOf(character, StringComparison.Ordinal);
            if (index < 0)
            {
                throw new FormatException("Invalid Base32 value.");
            }

            buffer = (buffer << 5) | index;
            bitsLeft += 5;
            if (bitsLeft >= 8)
            {
                bitsLeft -= 8;
                output.Add((byte)(buffer >> bitsLeft));
                buffer &= (1 << bitsLeft) - 1;
            }
        }

        return output.ToArray();
    }
}
