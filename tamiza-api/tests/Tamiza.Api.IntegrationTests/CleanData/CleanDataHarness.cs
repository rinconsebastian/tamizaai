using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.CleanData;

/// <summary>
/// A project with an admin, an analyst and a viewer whose published tables are written with SQL, as the analytics worker would,
/// next to the run records it leaves.
/// </summary>
public sealed class CleanDataHarness : IAsyncDisposable
{
    private CleanDataHarness(TamizaApiFactory factory, string db, Guid projectId, string exportDirectory, HttpClient viewer, HttpClient analyst, HttpClient admin)
    {
        Factory = factory;
        ExportDirectory = exportDirectory;
        Db = db;
        ProjectId = projectId;
        Viewer = viewer;
        Analyst = analyst;
        Admin = admin;
    }

    public TamizaApiFactory Factory { get; }

    /// <summary>Where this project's API keeps prepared export files; empty again when nothing waits for download.</summary>
    public string ExportDirectory { get; }

    public string[] ExportFiles => Directory.Exists(ExportDirectory) ? Directory.GetFiles(ExportDirectory) : [];

    public string Db { get; }

    public Guid ProjectId { get; }

    public HttpClient Viewer { get; }

    public HttpClient Analyst { get; }

    public HttpClient Admin { get; }

    public string Url => $"/api/v1/projects/{ProjectId}/data";

    public static async Task<CleanDataHarness> CreateAsync(
        PostgresFixture postgres, string? timeZone = null, Dictionary<string, string?>? settings = null, Action<IServiceCollection>? configureServices = null)
    {
        var exportDirectory = Path.Combine(Path.GetTempPath(), "tamiza-export-tests-" + Guid.NewGuid().ToString("N"));
        settings = new Dictionary<string, string?> { ["DataDashboard:ExportDirectory"] = exportDirectory }
            .Concat(settings ?? []).GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.Last().Value);
        // Idle connections of a finished test are pruned after a few seconds: the whole suite shares one server with a connection limit.
        var db = await postgres.CreateDatabaseAsync() + ";Connection Idle Lifetime=3;Connection Pruning Interval=1";
        var factory = new TamizaApiFactory(db, settings, configureServices);
        var adminId = await TestData.SignInAsync(factory, "ada", "ada@example.org");
        var projectId = await TestData.InsertProjectAsync(db, adminId);
        if (timeZone is not null)
        {
            await TestData.ExecuteAsync(db, "UPDATE tamiza.projects SET time_zone = @zone", ("zone", timeZone));
        }

        await TestData.AddMemberAsync(db, projectId, adminId, "admin");
        var analystId = await TestData.SignInAsync(factory, "grace", "grace@example.org");
        await TestData.AddMemberAsync(db, projectId, analystId, "analyst");
        var viewerId = await TestData.SignInAsync(factory, "vic", "vic@example.org");
        await TestData.AddMemberAsync(db, projectId, viewerId, "viewer");
        return new CleanDataHarness(factory, db, projectId, exportDirectory,
            TestData.ClientFor(factory, "vic", "vic@example.org"),
            TestData.ClientFor(factory, "grace", "grace@example.org"),
            TestData.ClientFor(factory, "ada", "ada@example.org"));
    }

    /// <summary>Replaces the project's schema of <paramref name="step"/> (<c>output</c> or <c>clean</c>) and records the step as succeeded.</summary>
    public Task<Guid> PublishAsync(string step, params (string Name, string Columns, string Rows)[] tables) =>
        PublishedTables.PublishAsync(Db, ProjectId, step, tables);

    public Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters) => TestData.ExecuteAsync(Db, sql, parameters);

    public string Schema(string step) => PublishedTables.Schema(ProjectId, step);

    public async Task<JsonElement> GetOverviewAsync(HttpClient? client = null)
    {
        var response = await (client ?? Viewer).GetAsync(Url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public async ValueTask DisposeAsync()
    {
        Viewer.Dispose();
        Analyst.Dispose();
        Admin.Dispose();
        await Factory.DisposeAsync();
        Performance.PerformanceHarness.ReleasePool(Db);
        if (Directory.Exists(ExportDirectory))
        {
            Directory.Delete(ExportDirectory, recursive: true);
        }
    }
}
