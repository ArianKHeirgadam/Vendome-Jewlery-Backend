using GoldInvoice.Application;
using GoldInvoice.Infrastructure;
using GoldInvoice.Infrastructure.LocalRuntime;
using GoldInvoice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GoldInvoice.IntegrationTests;

public sealed class LocalRuntimeTests : IDisposable
{
    private readonly string tempRoot;

    public LocalRuntimeTests()
    {
        tempRoot = Path.Combine(Path.GetTempPath(), $"vendome-localtests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
    }

    [Fact]
    public async Task LocalRuntime_DisabledModeRegistersInertHostedServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddLocalRuntime(CreateConfiguration(
            applyMigrations: false,
            backupEnabled: false));
        await using var provider = services.BuildServiceProvider();

        var hosted = provider.GetServices<IHostedService>();
        Assert.Contains(hosted, service => service is LocalDatabaseInitializer);
        Assert.Contains(hosted, service => service is LocalBackupWorker);

        var options = provider.GetRequiredService<IOptions<LocalRuntimeOptions>>().Value;
        Assert.False(options.ApplyMigrationsOnStartup);
        Assert.False(options.BackupEnabled);

        await provider.GetServices<IHostedService>()
            .Single(service => service is LocalDatabaseInitializer)
            .StartAsync(CancellationToken.None);
    }

    [Fact]
    public async Task LocalRuntime_InvalidBackupSettingsFailOnStartValidation()
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Configuration.AddConfiguration(CreateConfiguration(backupIntervalHours: 0));
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddLocalRuntime(builder.Configuration);
        using var host = builder.Build();

        await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
    }

    [Theory]
    [InlineData("%ProgramData%\\Vendome")]
    [InlineData(@"C:\Vendome Data")]
    public void LocalRuntimePaths_ResolvesToAnAbsoluteExpandedPath(string configured)
    {
        var resolved = LocalRuntimePaths.Resolve(configured);

        Assert.True(Path.IsPathRooted(resolved));
        Assert.False(configured.Contains('%') && resolved.Contains('%'));
    }

    [Fact]
    public void LocalRuntimePaths_RejectsEmptyDirectory()
    {
        Assert.Throws<ArgumentException>(() => LocalRuntimePaths.Resolve("   "));
    }

    [Fact]
    public void LocalBackupRetention_PrunesOldestBackupsBeyondTheLimit()
    {
        var directory = Directory.CreateDirectory(Path.Combine(tempRoot, "backups"));
        var names = Enumerable.Range(1, 4)
            .Select(index => $"vendome-{index:D8}.bak")
            .ToArray();
        foreach (var name in names)
        {
            File.WriteAllText(Path.Combine(directory.FullName, name), string.Empty);
        }

        // Give every file a distinct, explicit modification time.
        File.SetLastWriteTimeUtc(Path.Combine(directory.FullName, names[0]), DateTimeOffset.UtcNow.AddHours(-1).UtcDateTime);
        File.SetLastWriteTimeUtc(Path.Combine(directory.FullName, names[1]), DateTimeOffset.UtcNow.AddHours(-2).UtcDateTime);
        File.SetLastWriteTimeUtc(Path.Combine(directory.FullName, names[2]), DateTimeOffset.UtcNow.AddHours(-3).UtcDateTime);
        File.SetLastWriteTimeUtc(Path.Combine(directory.FullName, names[3]), DateTimeOffset.UtcNow.AddHours(-4).UtcDateTime);

        var pruned = LocalBackupRetention.Prune(directory.FullName, keep: 2);

        Assert.Equal(2, pruned.Count);
        Assert.Contains(Path.Combine(directory.FullName, names[2]), pruned);
        Assert.Contains(Path.Combine(directory.FullName, names[3]), pruned);
        var remaining = Directory.GetFiles(directory.FullName).Select(Path.GetFileName).ToArray();
        Assert.Equal(2, remaining.Length);
        Assert.Contains(names[0], remaining);
        Assert.Contains(names[1], remaining);
    }

    [Fact]
    public void LocalBackupRetention_KeepsEverythingWhenUnderTheLimit()
    {
        var directory = Directory.CreateDirectory(Path.Combine(tempRoot, "backups-few"));
        var path = Path.Combine(directory.FullName, "vendome-00000001.bak");
        File.WriteAllText(path, string.Empty);

        var pruned = LocalBackupRetention.Prune(directory.FullName, keep: 5);

        Assert.Empty(pruned);
        Assert.Single(Directory.GetFiles(directory.FullName));
    }

    [Fact]
    public void LocalBackupRetention_MissingDirectoryIsSafe()
    {
        Assert.Empty(LocalBackupRetention.Prune(
            Path.Combine(tempRoot, "does-not-exist"),
            keep: 3));
    }

    [Fact]
    public async Task LocalStorageHealthCheck_HealthyWhenFoldersAreWritable()
    {
        var provider = new LocalDataDirectoryProvider(Options.Create(new LocalRuntimeOptions
        {
            DataDirectory = Path.Combine(tempRoot, "data"),
            BackupDirectory = Path.Combine(tempRoot, "backups")
        }));
        var check = new LocalStorageHealthCheck(
            provider,
            NullLogger<LocalStorageHealthCheck>.Instance);

        var result = await check.CheckHealthAsync(
            new HealthCheckContext(),
            CancellationToken.None);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task LocalStorageHealthCheck_UnhealthyWhenFolderIsBlockedByAFile()
    {
        var blocked = Path.Combine(tempRoot, "blocked");
        File.WriteAllText(blocked, "this is a file, not a directory");
        var provider = new LocalDataDirectoryProvider(Options.Create(new LocalRuntimeOptions
        {
            DataDirectory = Path.Combine(blocked, "data"),
            BackupDirectory = Path.Combine(blocked, "backups")
        }));
        var check = new LocalStorageHealthCheck(
            provider,
            NullLogger<LocalStorageHealthCheck>.Instance);

        var result = await check.CheckHealthAsync(
            new HealthCheckContext(),
            CancellationToken.None);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task LocalDatabaseInitializer_MigratesAFreshDatabaseEndToEnd()
    {
        var databaseName = $"VendomeLocalE2E_{Guid.NewGuid():N}";
        var connection = $"Server=(localdb)\\MSSQLLocalDB;Database={databaseName};Integrated Security=True;Encrypt=True;TrustServerCertificate=True";
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:GoldInvoice"] = connection,
            ["Database:CommandTimeoutSeconds"] = "120",
            ["LocalRuntime:ApplyMigrationsOnStartup"] = "true",
            ["LocalRuntime:BackupEnabled"] = "false"
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddApplication();
        services.AddInfrastructure(configuration);
        services.AddLocalRuntime(configuration);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<GoldInvoiceDbContext>();

        if (!await context.Database.CanConnectAsync())
        {
            return; // Environment-gated: LocalDB is not installed on this machine.
        }

        try
        {
            var initializer = provider
                .GetServices<IHostedService>()
                .OfType<LocalDatabaseInitializer>()
                .Single();
            await initializer.StartAsync(CancellationToken.None);

            var allMigrations = context.Database.GetMigrations();
            var applied = await context.Database.GetAppliedMigrationsAsync();
            Assert.Equal(allMigrations, applied);
            Assert.Contains(applied, migration => migration.EndsWith("_InitialDomainModel", StringComparison.Ordinal));

            var tableCount = await context.Database
                .SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM [sys].[tables]")
                .SingleAsync();
            Assert.True(tableCount >= 30, $"Expected a fully migrated schema, found {tableCount} tables.");
        }
        finally
        {
            await context.Database.EnsureDeletedAsync();
        }
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup; failures are irrelevant to the tests.
        }
    }

    private static IConfiguration CreateConfiguration(
        bool applyMigrations = false,
        bool backupEnabled = false,
        double backupIntervalHours = 24)
    {
        var values = new Dictionary<string, string?>
        {
            ["LocalRuntime:DataDirectory"] = "%ProgramData%\\Vendome",
            ["LocalRuntime:ApplyMigrationsOnStartup"] = applyMigrations.ToString(),
            ["LocalRuntime:BackupEnabled"] = backupEnabled.ToString(),
            ["LocalRuntime:BackupIntervalHours"] = backupIntervalHours.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["LocalRuntime:BackupsToKeep"] = "14"
        };
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}