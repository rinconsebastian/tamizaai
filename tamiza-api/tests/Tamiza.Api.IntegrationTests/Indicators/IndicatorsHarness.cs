using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.Indicators;

/// <summary>
/// A project with an admin, an analyst and a viewer, plus a user who is not a member, whose indicator table is written with SQL
/// as the analytics worker would publish it, next to the run record it leaves. The project has its first dashboard, "Indicators",
/// which the widget helpers use unless given another.
/// </summary>
public sealed class IndicatorsHarness : IAsyncDisposable
{
    /// <summary>A typical indicator table: one dimension and the three conventional numeric columns.</summary>
    public const string StandardColumns = "municipality text, value numeric, numerator numeric, denominator numeric";

    private IndicatorsHarness(
        TamizaApiFactory factory, string db, Guid projectId, Guid dashboardId, string exportDirectory,
        HttpClient viewer, HttpClient analyst, HttpClient admin, HttpClient outsider)
    {
        Factory = factory;
        Db = db;
        ProjectId = projectId;
        DashboardId = dashboardId;
        ExportDirectory = exportDirectory;
        Viewer = viewer;
        Analyst = analyst;
        Admin = admin;
        Outsider = outsider;
    }

    public TamizaApiFactory Factory { get; }

    public string Db { get; }

    public Guid ProjectId { get; }

    /// <summary>The project's first dashboard, "Indicators", seeded with the project.</summary>
    public Guid DashboardId { get; }

    public string ExportDirectory { get; }

    public string[] ExportFiles => Directory.Exists(ExportDirectory) ? Directory.GetFiles(ExportDirectory) : [];

    public HttpClient Viewer { get; }

    public HttpClient Analyst { get; }

    public HttpClient Admin { get; }

    /// <summary>Signed in, but not a member of the project.</summary>
    public HttpClient Outsider { get; }

    public string Url => UrlOf(ProjectId);

    public static string UrlOf(Guid projectId) => $"/api/v1/projects/{projectId}/indicators";

    /// <summary>The project's dashboards: adding one, and <c>/order</c>.</summary>
    public string DashboardsUrl => $"/api/v1/projects/{ProjectId}/dashboards";

    /// <summary>The address of a dashboard of this project, the first one unless given another.</summary>
    public string DashboardUrl(Guid? dashboardId = null) => DashboardUrlOf(ProjectId, dashboardId ?? DashboardId);

    public static string DashboardUrlOf(Guid projectId, Guid dashboardId) => $"/api/v1/projects/{projectId}/dashboards/{dashboardId}";

    public string WidgetsUrl(Guid? dashboardId = null) => DashboardUrl(dashboardId) + "/widgets";

    public string WidgetUrl(Guid widgetId, Guid? dashboardId = null) => $"{WidgetsUrl(dashboardId)}/{widgetId}";

    public static async Task<IndicatorsHarness> CreateAsync(PostgresFixture postgres, Dictionary<string, string?>? settings = null)
    {
        var exportDirectory = Path.Combine(Path.GetTempPath(), "tamiza-indicator-export-tests-" + Guid.NewGuid().ToString("N"));
        settings = new Dictionary<string, string?> { ["DataDashboard:ExportDirectory"] = exportDirectory }
            .Concat(settings ?? []).GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.Last().Value);
        // Idle connections of a finished test are pruned after a few seconds: the whole suite shares one server with a connection limit.
        var db = await postgres.CreateDatabaseAsync() + ";Connection Idle Lifetime=3;Connection Pruning Interval=1";
        var factory = new TamizaApiFactory(db, settings);
        var adminId = await TestData.SignInAsync(factory, "ada", "ada@example.org");
        var projectId = await TestData.InsertProjectAsync(db, adminId, "Coverage 2026");
        await TestData.AddMemberAsync(db, projectId, adminId, "admin");
        var analystId = await TestData.SignInAsync(factory, "grace", "grace@example.org");
        await TestData.AddMemberAsync(db, projectId, analystId, "analyst");
        var viewerId = await TestData.SignInAsync(factory, "vic", "vic@example.org");
        await TestData.AddMemberAsync(db, projectId, viewerId, "viewer");
        await TestData.SignInAsync(factory, "mallory", "mallory@example.org");
        var dashboardId = await TestData.ScalarAsync<Guid>(db, "SELECT id FROM tamiza.dashboards WHERE project_id = @id", ("id", projectId));
        return new IndicatorsHarness(factory, db, projectId, dashboardId, exportDirectory,
            TestData.ClientFor(factory, "vic", "vic@example.org"),
            TestData.ClientFor(factory, "grace", "grace@example.org"),
            TestData.ClientFor(factory, "ada", "ada@example.org"),
            TestData.ClientFor(factory, "mallory", "mallory@example.org"));
    }

    /// <summary>The schema the worker publishes the project's indicator tables to.</summary>
    public string Schema => PublishedTables.Schema(ProjectId, "indicators");

    private readonly HashSet<string> _scripts = [];

    /// <summary>
    /// Creates the indicator script <paramref name="name"/> through the API as the analyst, once, so the overview lists it. The label
    /// is the name with its first letter capitalized unless one is given.
    /// </summary>
    public async Task AddIndicatorAsync(string name, string? label = null, string format = "decimal", string? unit = null, string? description = null)
    {
        if (!_scripts.Add(name))
        {
            return;
        }

        var response = await Analyst.PutAsJsonAsync(
            $"/api/v1/projects/{ProjectId}/scripts/indicators/{name}",
            new { code = "def compute(cleaned, ctx):\n    pass\n", label = label ?? char.ToUpperInvariant(name[0]) + name[1..], description, unit, format });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    /// <summary>
    /// Publishes the table <paramref name="name"/> as the worker's per-table publish does (the project's other indicator tables stay)
    /// and records the indicator's step as succeeded. Unless <paramref name="script"/> is false, the indicator script exists too.
    /// </summary>
    public async Task<Guid> PublishAsync(
        string name, string columns, string rows, string? label = null, string format = "decimal", string? unit = null, string? description = null, bool script = true)
    {
        if (script)
        {
            await AddIndicatorAsync(name, label, format, unit, description);
        }

        return await PublishedTables.PublishTableAsync(Db, ProjectId, "indicators", name, columns, rows);
    }

    public Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters) => TestData.ExecuteAsync(Db, sql, parameters);

    /// <summary>A second project of the same admin, with its first dashboard, for widgets that must stay in their own project.</summary>
    public async Task<(Guid ProjectId, Guid DashboardId)> AddOtherProjectAsync()
    {
        var adminId = await TestData.ScalarAsync<Guid>(Db, "SELECT id FROM tamiza.users WHERE keycloak_sub = 'ada'");
        var otherId = await TestData.InsertProjectAsync(Db, adminId, "Other");
        await TestData.AddMemberAsync(Db, otherId, adminId, "admin");
        var dashboardId = await TestData.ScalarAsync<Guid>(Db, "SELECT id FROM tamiza.dashboards WHERE project_id = @id", ("id", otherId));
        return (otherId, dashboardId);
    }

    /// <summary>Adds a dashboard after the project's others with SQL, as another member's session would leave it.</summary>
    public async Task<Guid> InsertDashboardAsync(string name)
    {
        var id = Guid.NewGuid();
        await ExecuteAsync(
            """
            INSERT INTO tamiza.dashboards (id, project_id, name, normalized_name, position, created_by, updated_by)
            SELECT @id, p.id, @name, lower(@name), (SELECT count(*) FROM tamiza.dashboards WHERE project_id = p.id), p.created_by, p.created_by
            FROM tamiza.projects AS p WHERE p.id = @projectId
            """,
            ("id", id), ("name", name), ("projectId", ProjectId));
        return id;
    }

    /// <summary>Removes a dashboard with SQL, as another member's removal would, with its widgets.</summary>
    public Task RemoveDashboardAsync(Guid dashboardId) =>
        ExecuteAsync("DELETE FROM tamiza.dashboards WHERE id = @id", ("id", dashboardId));

    /// <summary>Asks the API to add a dashboard named <paramref name="name"/>, as the analyst unless given another client.</summary>
    public Task<HttpResponseMessage> PostDashboardAsync(string name, HttpClient? client = null) =>
        (client ?? Analyst).PostAsJsonAsync(DashboardsUrl, new { name });

    /// <summary>Adds a dashboard through the API, after the project's others, and returns its id; fails the test when it is not created.</summary>
    public async Task<Guid> AddDashboardAsync(string name, HttpClient? client = null)
    {
        var response = await PostDashboardAsync(name, client);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    /// <summary>The project's dashboards as stored, in their order (position, then creation, then id).</summary>
    public async Task<(Guid Id, string Name, int Position)[]> DashboardsAsync() =>
        (await StoredDashboardsAsync())
            .Select(row => row.Split('|'))
            .Select(parts => (Guid.Parse(parts[1]), parts[2], int.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture)))
            .ToArray();

    /// <summary>
    /// The project's dashboards in their order as <c>position|id|name|normalized name|updated_by|updated_at</c>, to check that a refused
    /// request changed nothing.
    /// </summary>
    public async Task<string[]> StoredDashboardsAsync()
    {
        var rows = await TestData.ScalarAsync<string>(Db,
            """
            SELECT coalesce(string_agg(concat_ws('|', position, id, name, normalized_name, updated_by, updated_at), E'\n'
                                       ORDER BY position, created_at, id), '')
            FROM tamiza.dashboards WHERE project_id = @id
            """,
            ("id", ProjectId));
        return rows!.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// Every widget of the project, whatever its dashboard, as <c>title|dashboard id|chart type|config|layout|updated_at</c>, to check
    /// that a refused request changed nothing.
    /// </summary>
    public async Task<string[]> StoredWidgetsAsync()
    {
        var rows = await TestData.ScalarAsync<string>(Db,
            """
            SELECT coalesce(string_agg(concat_ws('|', title, dashboard_id, chart_type, config, layout, updated_at), E'\n' ORDER BY title, id), '')
            FROM tamiza.dashboard_widgets WHERE project_id = @id
            """,
            ("id", ProjectId));
        return rows!.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    public async Task<JsonElement> GetAsync(string path = "", HttpClient? client = null)
    {
        var response = await (client ?? Viewer).GetAsync(Url + path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>A dashboard of the project with its widgets (<c>{ id, name, widgets }</c>), the first one unless given another.</summary>
    public async Task<JsonElement> GetDashboardAsync(Guid? dashboardId = null, HttpClient? client = null)
    {
        var response = await (client ?? Viewer).GetAsync(DashboardUrl(dashboardId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>The widgets of a dashboard of the project, the first one unless given another.</summary>
    public async Task<JsonElement> GetWidgetsAsync(Guid? dashboardId = null, HttpClient? client = null) =>
        (await GetDashboardAsync(dashboardId, client)).GetProperty("widgets");

    /// <summary>Runs a widget's query (<paramref name="config"/> is the widget's <c>config</c>) and returns the response.</summary>
    public async Task<JsonElement> QueryAsync(object config, string? chartType = null, HttpClient? client = null)
    {
        var response = await PostQueryAsync(config, chartType, client);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public Task<HttpResponseMessage> PostQueryAsync(object config, string? chartType = null, HttpClient? client = null) =>
        (client ?? Viewer).PostAsJsonAsync(Url + "/query", new { config, chartType });

    /// <summary>Adds a widget to a dashboard of the project, the first one unless given another.</summary>
    public Task<HttpResponseMessage> PostWidgetAsync(object body, HttpClient? client = null, Guid? dashboardId = null) =>
        (client ?? Analyst).PostAsJsonAsync(WidgetsUrl(dashboardId), body);

    /// <summary>Adds a widget as the analyst and returns its JSON; fails the test when it is not created.</summary>
    public async Task<JsonElement> AddWidgetAsync(object body, HttpClient? client = null, Guid? dashboardId = null)
    {
        var response = await PostWidgetAsync(body, client, dashboardId);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public static string[] Errors(JsonElement problem, string field) =>
        problem.GetProperty("errors").GetProperty(field).EnumerateArray().Select(e => e.GetString()!).ToArray();

    /// <summary>The rows of a query response as <c>group|value</c>.</summary>
    public static string[] Rows(JsonElement result) =>
        result.GetProperty("rows").EnumerateArray()
            .Select(r => $"{r.GetProperty("group").GetString()}|{r.GetProperty("value").GetDouble().ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}")
            .ToArray();

    public static string[] Strings(JsonElement array) => array.EnumerateArray().Select(e => e.GetString()!).ToArray();

    public async ValueTask DisposeAsync()
    {
        Viewer.Dispose();
        Analyst.Dispose();
        Admin.Dispose();
        Outsider.Dispose();
        await Factory.DisposeAsync();
        Performance.PerformanceHarness.ReleasePool(Db);
        if (Directory.Exists(ExportDirectory))
        {
            Directory.Delete(ExportDirectory, recursive: true);
        }
    }
}

internal static class HttpClientPatchExtensions
{
    public static Task<HttpResponseMessage> PatchJsonAsync(this HttpClient client, string url, object body) =>
        client.PatchAsync(url, JsonContent.Create(body));
}
