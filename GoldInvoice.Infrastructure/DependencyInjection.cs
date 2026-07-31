using GoldInvoice.Infrastructure.Configuration;
using GoldInvoice.Infrastructure.Persistence;
using GoldInvoice.Infrastructure.Persistence.Interceptors;
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
}
