using Tamiza.Api.Auth;
using Tamiza.Api.Configuration;
using Tamiza.Api.Data;
using Tamiza.Api.Health;
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

if (!await app.MigrateDatabaseAsync())
{
    return 1;
}

await app.RunAsync();
return 0;
