using GoldInvoice.Api;
using GoldInvoice.Api.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace GoldInvoice.IntegrationTests;

public sealed class ApiFoundationTests
{
    [Fact]
    public async Task AddApiFoundation_RegistersAHealthySelfCheck()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApiFoundation(configuration);
        await using var serviceProvider = services.BuildServiceProvider();
        var healthCheckService = serviceProvider.GetRequiredService<HealthCheckService>();

        var report = await healthCheckService.CheckHealthAsync();

        Assert.Equal(HealthStatus.Healthy, report.Status);
        Assert.Contains("self", report.Entries.Keys);
    }

    [Fact]
    public void AddApiFoundation_RejectsAnInsecureRemoteCorsOrigin()
    {
        var settings = new Dictionary<string, string?>
        {
            [$"{ApiHostOptions.SectionName}:AllowedCorsOrigins:0"] = "http://example.com"
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApiFoundation(configuration);
        using var serviceProvider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() =>
            serviceProvider.GetRequiredService<IOptions<ApiHostOptions>>().Value);
    }
}
