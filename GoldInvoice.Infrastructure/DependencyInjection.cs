using GoldInvoice.Infrastructure.Configuration;
using GoldInvoice.Infrastructure.Identity;
using GoldInvoice.Infrastructure.Persistence;
using GoldInvoice.Infrastructure.Persistence.Interceptors;
using GoldInvoice.Infrastructure.Security;
using GoldInvoice.Application.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace GoldInvoice.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("GoldInvoice");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "The GoldInvoice database connection string is not configured.");
        }

        services
            .AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .Validate(
                options => options.CommandTimeoutSeconds is >= 1 and <= 300,
                "Database command timeout must be between 1 and 300 seconds.")
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<AuditingSaveChangesInterceptor>();
        services.AddDbContext<GoldInvoiceDbContext>((serviceProvider, options) =>
        {
            var databaseOptions = serviceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
            options.UseSqlServer(connectionString, sql =>
            {
                sql.CommandTimeout(databaseOptions.CommandTimeoutSeconds);
                sql.MigrationsAssembly(typeof(GoldInvoiceDbContext).Assembly.FullName);
            });
            options.EnableDetailedErrors(databaseOptions.EnableDetailedErrors);
            options.EnableSensitiveDataLogging(false);
            options.AddInterceptors(serviceProvider.GetRequiredService<AuditingSaveChangesInterceptor>());
        });

        services
            .AddHealthChecks()
            .AddDbContextCheck<GoldInvoiceDbContext>(
                name: "database",
                tags: ["ready"]);

        return services;
    }

    public static IServiceCollection AddSecurityInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var securitySection = configuration.GetSection(IdentitySecurityOptions.SectionName);
        var securitySettings = securitySection.Get<IdentitySecurityOptions>() ?? new IdentitySecurityOptions();

        services
            .AddOptions<IdentitySecurityOptions>()
            .Bind(securitySection)
            .Validate(IdentitySecurityOptions.IsValid, "Identity security settings are invalid.")
            .ValidateOnStart();
        services
            .AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(JwtOptions.IsValid, "JWT settings are invalid or the signing key is too weak.")
            .ValidateOnStart();
        services
            .AddOptions<BootstrapOwnerOptions>()
            .Bind(configuration.GetSection(BootstrapOwnerOptions.SectionName))
            .Validate(BootstrapOwnerOptions.IsValid, "Owner bootstrap settings are invalid.")
            .ValidateOnStart();

        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.Password.RequiredLength = securitySettings.PasswordRequiredLength;
                options.Password.RequiredUniqueChars = 4;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = securitySettings.MaxFailedAccessAttempts;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(securitySettings.LockoutMinutes);
                options.SignIn.RequireConfirmedEmail = true;
                options.User.RequireUniqueEmail = true;
            })
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<GoldInvoiceDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        services.Configure<PasswordHasherOptions>(options =>
        {
            options.CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3;
            options.IterationCount = 210_000;
        });
        services.Configure<DataProtectionTokenProviderOptions>(options =>
        {
            options.TokenLifespan = TimeSpan.FromHours(1);
        });

        services.AddDataProtection();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IDummyPasswordVerifier, DummyPasswordVerifier>();
        services.AddScoped<ISecurityTokenService, SecurityTokenService>();
        services.AddScoped<IAccountAuthenticationService, AccountAuthenticationService>();
        services.AddScoped<IAccessTokenPrincipalValidator, AccessTokenPrincipalValidator>();
        services.AddHostedService<SecurityBootstrapHostedService>();

        return services;
    }
}
