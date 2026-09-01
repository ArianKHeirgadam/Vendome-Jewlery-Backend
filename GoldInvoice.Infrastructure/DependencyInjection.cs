using GoldInvoice.Application.Catalog;
using GoldInvoice.Application.Business;
using GoldInvoice.Application.Customers;
using GoldInvoice.Application.Devices;
using GoldInvoice.Application.Inventory;
using GoldInvoice.Application.Invoicing;
using GoldInvoice.Application.Integration;
using GoldInvoice.Application.Orders;
using GoldInvoice.Application.Payments;
using GoldInvoice.Application.People;
using GoldInvoice.Application.Pricing;
using GoldInvoice.Application.Security;
using GoldInvoice.Application.Settings;
using GoldInvoice.Infrastructure.Catalog;
using GoldInvoice.Infrastructure.Business;
using GoldInvoice.Infrastructure.Configuration;
using GoldInvoice.Infrastructure.Customers;
using GoldInvoice.Infrastructure.Identity;
using GoldInvoice.Infrastructure.Inventory;
using GoldInvoice.Infrastructure.Invoicing;
using GoldInvoice.Infrastructure.Integration;
using GoldInvoice.Infrastructure.Orders;
using GoldInvoice.Infrastructure.Payments;
using GoldInvoice.Infrastructure.People;
using GoldInvoice.Infrastructure.Persistence;
using GoldInvoice.Infrastructure.Persistence.Interceptors;
using GoldInvoice.Infrastructure.Platform;
using GoldInvoice.Infrastructure.Pricing;
using GoldInvoice.Infrastructure.Security;
using GoldInvoice.Infrastructure.Settings;
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
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("GoldInvoice");
        if (string.IsNullOrWhiteSpace(connectionString)) throw new InvalidOperationException("The GoldInvoice database connection string is not configured.");

        services.AddOptions<DatabaseOptions>().Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .Validate(options => options.CommandTimeoutSeconds is >= 1 and <= 300, "Database command timeout must be between 1 and 300 seconds.").ValidateOnStart();
        services.AddOptions<MarketPriceOptions>().Bind(configuration.GetSection(MarketPriceOptions.SectionName)).Validate(MarketPriceOptions.IsValid, "Market-price settings are invalid.").ValidateOnStart();
        services.AddOptions<PaymentProcessingOptions>().Bind(configuration.GetSection(PaymentProcessingOptions.SectionName)).Validate(PaymentProcessingOptions.IsValid, "Payment-processing settings are invalid.").ValidateOnStart();
        services.AddOptions<InvoicingOptions>().Bind(configuration.GetSection(InvoicingOptions.SectionName)).Validate(InvoicingOptions.IsValid, "Invoice-sequence settings are invalid.").ValidateOnStart();
        services.AddOptions<OutboxOptions>().Bind(configuration.GetSection(OutboxOptions.SectionName)).Validate(OutboxOptions.IsValid, "Outbox settings are invalid.").ValidateOnStart();
        services.AddOptions<ProductImageStorageOptions>().Bind(configuration.GetSection(ProductImageStorageOptions.SectionName));

        services.TryAddSingleton(TimeProvider.System);
        services.AddHttpContextAccessor();
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

        services.AddHealthChecks().AddDbContextCheck<GoldInvoiceDbContext>(name: "database", tags: ["ready"]);
        services.AddScoped<ICatalogService, CatalogService>();
        services.AddSingleton<IProductImageStorage, LocalProductImageStorage>();
        services.AddScoped<IProductImageService, ProductImageService>();
        services.AddScoped<IProductPricingService, ProductPricingService>();
        services.AddScoped<IMarketPriceIngestionService, MarketPriceIngestionService>();
        services.AddScoped<IInventoryService, InventoryService>();
        services.AddScoped<ISupplierPurchaseService, SupplierPurchaseService>();
        services.AddScoped<InventoryReservationCoordinator>();
        services.AddScoped<ICustomerAddressService, CustomerAddressService>();
        services.AddScoped<ISupplierService, SupplierService>();
        services.AddScoped<ICustomerInteractionService, CustomerInteractionService>();
        services.AddScoped<IStoreProfileService, StoreProfileService>();
        services.AddScoped<IFinancialWorkspaceService, FinancialWorkspaceService>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<InvoiceService>();
        services.AddScoped<IInvoiceService>(provider => provider.GetRequiredService<InvoiceService>());
        services.AddScoped<IInvoiceIssuanceService>(provider => provider.GetRequiredService<InvoiceService>());
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<IFlexiblePaymentService, FlexiblePaymentService>();
        services.AddScoped<IBankInterestService, BankInterestService>();
        services.AddScoped<IOutboxWriter, OutboxWriter>();
        services.AddScoped<IOutboxStore, OutboxStore>();
        services.AddSingleton<IOutboxDispatcher, OutboxDispatcher>();
        services.AddScoped<IOutboxAdministrationService, OutboxAdministrationService>();
        services.AddScoped<IIntegrationEventQueryService, IntegrationEventQueryService>();
        services.AddScoped<IDataRetentionService, DataRetentionService>();
        services.AddScoped<IDeviceSynchronizationService, DeviceSynchronizationService>();

        return services;
    }

    public static IServiceCollection AddOutboxProcessing(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddHostedService<OutboxDispatchHostedService>();
        services.AddHostedService<OutboxSqlDiagnosticHostedService>();
        return services;
    }

    public static IServiceCollection AddSecurityInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        var securitySection = configuration.GetSection(IdentitySecurityOptions.SectionName);
        var securitySettings = securitySection.Get<IdentitySecurityOptions>() ?? new IdentitySecurityOptions();
        services.AddOptions<IdentitySecurityOptions>().Bind(securitySection).Validate(IdentitySecurityOptions.IsValid, "Identity security settings are invalid.").ValidateOnStart();
        services.AddOptions<JwtOptions>().Bind(configuration.GetSection(JwtOptions.SectionName)).Validate(JwtOptions.IsValid, "JWT settings are invalid or the signing key is too weak.").ValidateOnStart();
        services.AddOptions<BootstrapOwnerOptions>().Bind(configuration.GetSection(BootstrapOwnerOptions.SectionName)).Validate(BootstrapOwnerOptions.IsValid, "Owner bootstrap settings are invalid.").ValidateOnStart();
        services.AddIdentityCore<ApplicationUser>(options =>
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
            options.User.RequireUniqueEmail = false;
        }).AddRoles<ApplicationRole>().AddEntityFrameworkStores<GoldInvoiceDbContext>().AddUserStore<ProtectedIdentityUserStore>().AddUserValidator<OptionalEmailUserValidator>().AddSignInManager().AddDefaultTokenProviders();
        services.Configure<PasswordHasherOptions>(options => { options.CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3; options.IterationCount = 210_000; });
        services.Configure<DataProtectionTokenProviderOptions>(options => options.TokenLifespan = TimeSpan.FromHours(1));
        services.AddDataProtection().SetApplicationName("GoldInvoice");
        services.TryAddSingleton(TimeProvider.System);
        services.AddMemoryCache();
        services.AddSingleton<AccessResolutionCache>();
        services.AddSingleton<IDummyPasswordVerifier, DummyPasswordVerifier>();
        services.AddScoped<ISecurityTokenService, SecurityTokenService>();
        services.AddScoped<IAccountAuthenticationService, AccountAuthenticationService>();
        services.AddScoped<IAccessTokenPrincipalValidator, AccessTokenPrincipalValidator>();
        services.AddScoped<IPeopleDirectoryService, PeopleDirectoryService>();
        services.AddHostedService<SecurityBootstrapHostedService>();
        return services;
    }
}
