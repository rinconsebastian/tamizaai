using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tamiza.Api.Maps;

namespace Tamiza.Api.IntegrationTests.Infrastructure;

/// <summary>A clock a test sets and moves, starting at a given instant. Unlike <see cref="AdjustableTimeProvider"/> it stands still between calls.</summary>
public sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private long _ticks = start.UtcTicks;

    public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);

    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);
}

/// <summary>
/// The api with basemaps configured and Google replaced by a <see cref="GoogleStub"/>, on a manual clock that starts five minutes into a
/// tile-link window. Two project members, one user of the realm who belongs to no project, and an anonymous client.
/// </summary>
public sealed class MapsHarness : IAsyncDisposable
{
    public const string Streets = "streets";
    public const string Satellite = "satellite";

    private MapsHarness(TamizaApiFactory factory, string db, GoogleStub google, ManualTimeProvider time)
    {
        Factory = factory;
        Db = db;
        Google = google;
        Time = time;
        Member = TestData.ClientFor(factory, "ada", "ada@example.org");
        OtherMember = TestData.ClientFor(factory, "grace", "grace@example.org");
        Outsider = TestData.ClientFor(factory, "mallory", "mallory@example.org");
        Anonymous = factory.CreateClient();
    }

    public TamizaApiFactory Factory { get; }

    public string Db { get; }

    public GoogleStub Google { get; }

    public ManualTimeProvider Time { get; }

    public HttpClient Member { get; }

    public HttpClient OtherMember { get; }

    /// <summary>Signed in, but a member of no project.</summary>
    public HttpClient Outsider { get; }

    public HttpClient Anonymous { get; }

    public static Dictionary<string, string?> GoogleSettings(string provider = "google") => new()
    {
        ["TAMIZA_MAP_BASEMAP_PROVIDER"] = provider,
        ["TAMIZA_MAP_GOOGLE_API_KEY"] = GoogleStub.Key,
    };

    /// <summary>The instant a window of <paramref name="window"/> starts at or before <paramref name="now"/>.</summary>
    public static DateTimeOffset WindowStart(DateTimeOffset now, TimeSpan? window = null)
    {
        var seconds = (long)(window ?? TimeSpan.FromMinutes(45)).TotalSeconds;
        return DateTimeOffset.FromUnixTimeSeconds(now.ToUnixTimeSeconds() / seconds * seconds);
    }

    public static async Task<MapsHarness> CreateAsync(
        PostgresFixture postgres, Dictionary<string, string?>? settings = null, Action<IServiceCollection>? configure = null)
    {
        var db = await postgres.CreateDatabaseAsync() + ";Connection Idle Lifetime=3;Connection Pruning Interval=1";
        var google = new GoogleStub();
        var time = new ManualTimeProvider(WindowStart(DateTimeOffset.UtcNow) + TimeSpan.FromMinutes(5));
        var factory = new TamizaApiFactory(db, settings, services =>
        {
            services.AddSingleton<TimeProvider>(time);
            services.AddHttpClient(GoogleHttp.ClientName).ConfigurePrimaryHttpMessageHandler(() => google);
            configure?.Invoke(services);
        });
        var adminId = await TestData.SignInAsync(factory, "ada", "ada@example.org");
        var projectId = await TestData.InsertProjectAsync(db, adminId, "Coverage 2026");
        await TestData.AddMemberAsync(db, projectId, adminId, "admin");
        var otherId = await TestData.SignInAsync(factory, "grace", "grace@example.org");
        await TestData.AddMemberAsync(db, projectId, otherId, "viewer");
        await TestData.SignInAsync(factory, "mallory", "mallory@example.org");
        return new MapsHarness(factory, db, google, time);
    }

    /// <summary>The list as the user gets it; fails the test when it is not a 200.</summary>
    public async Task<JsonElement> ListAsync(HttpClient? client = null)
    {
        var response = await (client ?? Member).GetAsync("/api/v1/basemaps");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>The <c>tiles</c> template of a kind in a list.</summary>
    public static string TemplateOf(JsonElement list, string kind) =>
        list.GetProperty("basemaps").EnumerateArray().Single(b => b.GetProperty("kind").GetString() == kind).GetProperty("tiles").GetString()!;

    /// <summary>The path of a tile from a template: <c>/api/v1/basemap-tiles/&lt;link&gt;/z/x/y</c>.</summary>
    public static string TilePath(string template, string z = "3", string x = "2", string y = "4") =>
        template.Replace("{z}", z).Replace("{x}", x).Replace("{y}", y);

    /// <summary>The link inside a tile template.</summary>
    public static string LinkOf(string template) => template.Split('/')[4];

    public async ValueTask DisposeAsync()
    {
        Member.Dispose();
        OtherMember.Dispose();
        Outsider.Dispose();
        Anonymous.Dispose();
        await Factory.DisposeAsync();
        Performance.PerformanceHarness.ReleasePool(Db);
    }
}
