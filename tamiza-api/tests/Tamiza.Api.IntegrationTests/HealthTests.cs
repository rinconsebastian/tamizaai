using System.Diagnostics;
using System.Net;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class HealthTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Live_and_ready_report_healthy_against_a_migrated_database()
    {
        await using var factory = new TamizaApiFactory(await postgres.CreateDatabaseAsync());
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
    }

    [Fact]
    public async Task Ready_reports_unhealthy_after_the_database_stops()
    {
        // A dedicated container, because this test stops it.
        await using var container = PostgresFixture.CreateContainer();
        await container.StartAsync();
        await using var factory = new TamizaApiFactory(container.GetConnectionString() + ";Timeout=3");
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);

        await container.StopAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health/ready")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
    }

    [Fact]
    public async Task Process_exits_non_zero_without_serving_when_the_database_is_unreachable()
    {
        var api = Path.Combine(AppContext.BaseDirectory, "Tamiza.Api.dll");
        var start = new ProcessStartInfo("dotnet", [api])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            Environment =
            {
                ["ConnectionStrings__Tamiza"] = "Host=127.0.0.1;Port=1;Database=tamiza;Username=tamiza;Password=x;Timeout=2",
                ["TAMIZA_PUBLIC_URL"] = "https://tamiza.test",
                ["TAMIZA_DATA_PROTECTION_KEYS_PATH"] = Path.GetTempPath(),
                ["OIDC_AUTHORITY"] = "https://idp.test/realms/tamiza",
                ["OIDC_CLIENT_ID"] = "tamiza-ui",
                ["OIDC_AUDIENCE"] = "tamiza-api",
                ["ASPNETCORE_URLS"] = "http://127.0.0.1:0",
            },
        };

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await process.WaitForExitAsync(timeout.Token);

        Assert.NotEqual(0, process.ExitCode);
        Assert.Contains("Database migration failed", await output);
        Assert.DoesNotContain("Now listening on", await output);
    }
}
