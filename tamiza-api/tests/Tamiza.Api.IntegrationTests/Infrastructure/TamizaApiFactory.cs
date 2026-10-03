using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Tamiza.Api.IntegrationTests.Infrastructure;

/// <summary>Runs the API in memory against a test database, with the settings Compose would provide.</summary>
public sealed class TamizaApiFactory(string connectionString, IReadOnlyDictionary<string, string?>? overrides = null)
    : WebApplicationFactory<Program>
{
    public string KeysPath { get; } = Directory.CreateTempSubdirectory("tamiza-keys-").FullName;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Tamiza"] = connectionString,
            ["TAMIZA_PUBLIC_URL"] = "https://tamiza.test",
            ["TAMIZA_DATA_PROTECTION_KEYS_PATH"] = KeysPath,
            ["OIDC_AUTHORITY"] = TestTokens.Issuer,
            ["OIDC_CLIENT_ID"] = "tamiza-ui",
            ["OIDC_AUDIENCE"] = TestTokens.Audience,
        };
        foreach (var (key, value) in overrides ?? new Dictionary<string, string?>())
        {
            settings[key] = value;
        }

        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, TestTokens.TrustTestKey));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(KeysPath))
        {
            Directory.Delete(KeysPath, recursive: true);
        }
    }
}
