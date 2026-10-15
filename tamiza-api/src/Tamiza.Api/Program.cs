using Tamiza.Api.Auth;
using Tamiza.Api.CleanData;
using Tamiza.Api.Configuration;
using Tamiza.Api.Data;
using Tamiza.Api.Health;
using Tamiza.Api.Indicators;
using Tamiza.Api.Ingestion;
using Tamiza.Api.Performance;
using Tamiza.Api.Pipeline;
using Tamiza.Api.Projects;
using Tamiza.Api.Security;
using Tamiza.Api.SystemInfo;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTamizaOptions();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddTamizaDatabase();
builder.Services.AddTamizaDataProtection();
builder.Services.AddTamizaAuthentication();
builder.Services.AddTamizaHealthChecks();
builder.Services.AddTamizaProjects();
builder.Services.AddTamizaIngestion();
builder.Services.AddTamizaPerformance();
builder.Services.AddTamizaCleanData();
builder.Services.AddTamizaIndicators();
builder.Services.AddTamizaPipeline();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseUserProvisioning();
app.UseAuthorization();

app.MapTamizaHealthChecks();
app.MapOpenApi("/api/v1/openapi.json").AllowAnonymous();

var api = app.MapGroup("/api/v1");
api.MapSystemEndpoints();
api.MapMeEndpoints();
api.MapProjectEndpoints();
api.MapMemberEndpoints();
api.MapSamplingFrameEndpoints();
api.MapWebhookEndpoints();
api.MapIngestionEndpoints();
api.MapPerformanceEndpoints();
api.MapDataEndpoints();
api.MapIndicatorEndpoints();
api.MapScriptEndpoints();
api.MapRunEndpoints();
api.MapReferenceTableEndpoints();
api.MapScriptTestEndpoints();
api.MapSampleDownloadEndpoints();
api.MapScriptInputEndpoints();

if (!await app.MigrateDatabaseAsync())
{
    return 1;
}

await app.RunAsync();
return 0;
