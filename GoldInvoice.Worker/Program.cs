using GoldInvoice.Application;
using GoldInvoice.Infrastructure;
using GoldInvoice.Infrastructure.Configuration;
using GoldInvoice.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";
    options.UseUtcTimestamp = true;
});

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddLocalRuntime(builder.Configuration);
builder.Services
    .AddOptions<WorkerScheduleOptions>()
    .Bind(builder.Configuration.GetSection(WorkerScheduleOptions.SectionName))
    .Validate(WorkerScheduleOptions.IsValid, "Worker schedules are invalid.")
    .ValidateOnStart();
builder.Services
    .AddOptions<RetentionOptions>()
    .Bind(builder.Configuration.GetSection(RetentionOptions.SectionName))
    .Validate(RetentionOptions.IsValid, "Data-retention settings are invalid.")
    .ValidateOnStart();
builder.Services.AddHostedService<MarketPriceWorker>();
builder.Services.AddHostedService<ReservationExpirationWorker>();
builder.Services.AddHostedService<DataRetentionWorker>();

var host = builder.Build();
host.Run();
