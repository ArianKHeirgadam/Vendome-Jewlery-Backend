using GoldInvoice.Api;
using GoldInvoice.Api.Configuration;
using GoldInvoice.Api.Integration;
using GoldInvoice.Api.Middleware;
using GoldInvoice.Api.Security;
using GoldInvoice.Application;
using GoldInvoice.Application.Integration;
using GoldInvoice.Infrastructure;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";
    options.UseUtcTimestamp = true;
});

builder.Services.AddControllers();
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = false;
    options.MaximumReceiveMessageSize = 32 * 1024;
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme
        {
            Reference = new OpenApiReference
            {
                Type = ReferenceType.SecurityScheme,
                Id = "Bearer"
            }
        }] = []
    });
});
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddSingleton<IIntegrationEventHandler, SignalRIntegrationEventHandler>();
builder.Services.AddSecurityInfrastructure(builder.Configuration);
builder.Services.AddApiFoundation(builder.Configuration);
builder.Services.AddApiSecurity(builder.Configuration);
builder.Services.AddOutboxProcessing();

var app = builder.Build();
var apiOptions = app.Services.GetRequiredService<IOptions<ApiHostOptions>>().Value;

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();
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

app.UseStatusCodePages(async statusCodeContext =>
{
    var httpContext = statusCodeContext.HttpContext;
    var problemDetailsService = httpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
    await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
    {
        HttpContext = httpContext,
        ProblemDetails = new ProblemDetails
        {
            Status = httpContext.Response.StatusCode,
            Title = httpContext.Response.StatusCode switch
            {
                StatusCodes.Status401Unauthorized => "Authentication required.",
                StatusCodes.Status403Forbidden => "Access denied.",
                StatusCodes.Status404NotFound => "Resource not found.",
                _ => "The request could not be processed."
            }
        }
    });
});
app.UseHttpsRedirection();
app.UseRouting();
app.UseCors(ApiServiceCollectionExtensions.CorsPolicyName);
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<IntegrationHub>(IntegrationHub.Route);
app.MapHealthChecks(apiOptions.LivenessPath, new HealthCheckOptions
{
    AllowCachingResponses = false,
    Predicate = registration => registration.Tags.Contains("live")
}).AllowAnonymous();
app.MapHealthChecks(apiOptions.ReadinessPath, new HealthCheckOptions
{
    AllowCachingResponses = false
}).AllowAnonymous();

app.Run();

public partial class Program;
