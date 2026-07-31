using GoldInvoice.Api;
using GoldInvoice.Api.Configuration;
using GoldInvoice.Api.Middleware;
using GoldInvoice.Application;
using GoldInvoice.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";
    options.UseUtcTimestamp = true;
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApiFoundation(builder.Configuration);

var app = builder.Build();
var apiOptions = app.Services.GetRequiredService<IOptions<ApiHostOptions>>().Value;

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseHsts();
}

app.UseStatusCodePages();
app.UseHttpsRedirection();
app.UseCors(ApiServiceCollectionExtensions.CorsPolicyName);
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks(apiOptions.LivenessPath, new HealthCheckOptions
{
    AllowCachingResponses = false,
    Predicate = registration => registration.Tags.Contains("live")
});
app.MapHealthChecks(apiOptions.ReadinessPath, new HealthCheckOptions
{
    AllowCachingResponses = false
});

app.Run();

public partial class Program;
