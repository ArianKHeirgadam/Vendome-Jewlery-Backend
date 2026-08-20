using GoldInvoice.Infrastructure.LocalRuntime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GoldInvoice.Infrastructure;

public static class LocalRuntimeServiceCollectionExtensions
{
    /// <summary>
    /// Registers the per-machine local-runtime services: startup migrations,
    /// scheduled SQL Server backups with retention, and the local-storage
    /// health check. Everything is inert unless the LocalRuntime
    /// configuration section enables it, so existing deployments are
    /// unaffected. Call this after <c>AddInfrastructure</c> and before any
    /// other hosted services that require an initialized database.
    /// </summary>
    public static IServiceCollection AddLocalRuntime(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(LocalRuntimeOptions.SectionName);
        var settings = section.Get<LocalRuntimeOptions>() ?? new LocalRuntimeOptions();
        services
            .AddOptions<LocalRuntimeOptions>()
            .Bind(section)
            .Validate(LocalRuntimeOptions.IsValid, "Local-runtime settings are invalid.")
            .ValidateOnStart();

        services.AddSingleton<ILocalDataDirectoryProvider, LocalDataDirectoryProvider>();
        services.AddHostedService<LocalDatabaseInitializer>();
        services.AddHostedService<LocalBackupWorker>();

        if (settings.ApplyMigrationsOnStartup || settings.BackupEnabled)
        {
            services.AddHealthChecks()
                .AddCheck<LocalStorageHealthCheck>(
                    name: "local-storage",
                    tags: ["ready"]);
        }

        return services;
    }
}