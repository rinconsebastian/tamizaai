using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.Maps;

/// <summary><c>GET /api/v1/basemaps</c>: what a deployment offers, to whom, and the tile links it issues.</summary>
[Collection(PostgresCollection.Name)]
public sealed class BasemapListTests(PostgresFixture postgres)
{
    [Fact]
    public async Task A_request_without_a_token_is_401()
    {
        await using var h = await MapsHarness.CreateAsync(postgres, MapsHarness.GoogleSettings());

        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Anonymous.GetAsync("/api/v1/basemaps")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Anonymous.GetAsync("/api/v1/basemaps/streets/credits?zoom=1&north=1&south=0&east=1&west=0")).StatusCode);
    }

    [Fact]
    public async Task A_deployment_without_basemaps_answers_none_and_an_empty_list()
    {
        await using var h = await MapsHarness.CreateAsync(postgres);

        var list = await h.ListAsync();

        Assert.Equal("none", list.GetProperty("provider").GetString());
        Assert.Equal(0, list.GetProperty("basemaps").GetArrayLength());
        Assert.Empty(h.Google.Calls);
    }

    [Fact]
    public async Task A_user_without_a_project_gets_an_empty_list_and_no_link_even_under_google()
    {
        await using var h = await MapsHarness.CreateAsync(postgres, MapsHarness.GoogleSettings());

        var outsider = await h.ListAsync(h.Outsider);
        var member = await h.ListAsync(h.Member);

        Assert.Equal("google", outsider.GetProperty("provider").GetString());
        Assert.Equal(0, outsider.GetProperty("basemaps").GetArrayLength());
        Assert.DoesNotContain("basemap-tiles", outsider.GetRawText());
        Assert.Equal(2, member.GetProperty("basemaps").GetArrayLength());
    }

    [Fact]
    public async Task A_superadmin_without_a_project_gets_the_list()
    {
        var settings = MapsHarness.GoogleSettings();
        settings["TAMIZA_SUPERADMIN_EMAILS"] = "mallory@example.org";
        await using var h = await MapsHarness.CreateAsync(postgres, settings);

        var list = await h.ListAsync(h.Outsider);

        Assert.Equal(2, list.GetProperty("basemaps").GetArrayLength());
    }

    [Fact]
    public async Task OpenStreetMap_offers_streets_with_its_template_zoom_19_and_its_credit()
    {
        await using var h = await MapsHarness.CreateAsync(postgres, MapsHarness.GoogleSettings("osm"));

        var list = await h.ListAsync();

        Assert.Equal("osm", list.GetProperty("provider").GetString());
        var streets = Assert.Single(list.GetProperty("basemaps").EnumerateArray());
        Assert.Equal("streets", streets.GetProperty("kind").GetString());
        Assert.Equal((0, 19), (streets.GetProperty("minZoom").GetInt32(), streets.GetProperty("maxZoom").GetInt32()));
        Assert.Equal("https://tile.openstreetmap.org/{z}/{x}/{y}.png", streets.GetProperty("tiles").GetString());
        Assert.Equal(JsonValueKind.Null, streets.GetProperty("linksExpireAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, streets.GetProperty("linksRefreshInSeconds").ValueKind);
        var attribution = streets.GetProperty("attribution");
        Assert.Equal("osm", attribution.GetProperty("source").GetString());
        Assert.Equal("© OpenStreetMap contributors", attribution.GetProperty("text").GetString());
        Assert.Equal("https://www.openstreetmap.org/copyright", attribution.GetProperty("url").GetString());
        Assert.Empty(h.Google.Calls);
    }

    [Fact]
    public async Task Google_offers_streets_and_satellite_with_links_and_never_shows_the_key_or_calls_google()
    {
        await using var h = await MapsHarness.CreateAsync(postgres, MapsHarness.GoogleSettings());

        var list = await h.ListAsync();

        Assert.Equal("google", list.GetProperty("provider").GetString());
        var basemaps = list.GetProperty("basemaps").EnumerateArray().ToArray();
        Assert.Equal(["streets", "satellite"], basemaps.Select(b => b.GetProperty("kind").GetString()).ToArray());
        foreach (var basemap in basemaps)
        {
            Assert.Equal((0, 22), (basemap.GetProperty("minZoom").GetInt32(), basemap.GetProperty("maxZoom").GetInt32()));
            Assert.StartsWith("/api/v1/basemap-tiles/", basemap.GetProperty("tiles").GetString());
            Assert.EndsWith("/{z}/{x}/{y}", basemap.GetProperty("tiles").GetString());
            Assert.Equal("google", basemap.GetProperty("attribution").GetProperty("source").GetString());
            Assert.Equal(JsonValueKind.Null, basemap.GetProperty("attribution").GetProperty("text").ValueKind);
        }

        Assert.DoesNotContain(GoogleStub.Key, list.GetRawText());
        Assert.NotEqual(MapsHarness.LinkOf(MapsHarness.TemplateOf(list, "streets")), MapsHarness.LinkOf(MapsHarness.TemplateOf(list, "satellite")));
        Assert.Empty(h.Google.Calls); // links are made without a session
    }

    [Fact]
    public async Task The_links_expire_a_quarter_of_an_hour_after_their_window_and_say_when_to_ask_again()
    {
        await using var h = await MapsHarness.CreateAsync(postgres, MapsHarness.GoogleSettings());
        var windowEnd = MapsHarness.WindowStart(h.Time.GetUtcNow()) + TimeSpan.FromMinutes(45);

        var streets = (await h.ListAsync()).GetProperty("basemaps")[0];

        Assert.Equal(windowEnd + TimeSpan.FromMinutes(15), streets.GetProperty("linksExpireAt").GetDateTimeOffset());
        Assert.Equal((int)(windowEnd - h.Time.GetUtcNow()).TotalSeconds, streets.GetProperty("linksRefreshInSeconds").GetInt32());
    }

    [Fact]
    public async Task Reloading_within_a_window_gives_the_same_links_and_the_next_window_a_new_one()
    {
        await using var h = await MapsHarness.CreateAsync(postgres, MapsHarness.GoogleSettings());

        var first = await h.ListAsync();
        h.Time.Advance(TimeSpan.FromMinutes(20));
        var second = await h.ListAsync();
        var otherUser = await h.ListAsync(h.OtherMember);
        h.Time.Advance(TimeSpan.FromMinutes(30)); // 55 minutes after the first call: the next window
        var next = await h.ListAsync();

        foreach (var kind in new[] { "streets", "satellite" })
        {
            Assert.Equal(MapsHarness.TemplateOf(first, kind), MapsHarness.TemplateOf(second, kind));
            Assert.NotEqual(MapsHarness.TemplateOf(first, kind), MapsHarness.TemplateOf(next, kind));
            Assert.NotEqual(MapsHarness.TemplateOf(first, kind), MapsHarness.TemplateOf(otherUser, kind));
        }
    }

    [Fact]
    public async Task The_public_system_configuration_says_nothing_about_basemaps()
    {
        await using var h = await MapsHarness.CreateAsync(postgres, MapsHarness.GoogleSettings());

        var config = await h.Anonymous.GetStringAsync("/api/v1/system/config");

        Assert.DoesNotContain("map", config, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("google", config, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(GoogleStub.Key, config);
    }

    [Fact]
    public async Task An_unknown_provider_stops_the_api_at_startup_naming_the_variable()
    {
        var settings = new Dictionary<string, string?> { ["TAMIZA_MAP_BASEMAP_PROVIDER"] = "mapbox" };

        var failure = await Assert.ThrowsAnyAsync<Exception>(() => MapsHarness.CreateAsync(postgres, settings));

        Assert.Contains("TAMIZA_MAP_BASEMAP_PROVIDER", failure.ToString());
        Assert.Contains("none, osm or google", failure.ToString());
    }

    [Fact]
    public async Task Google_without_a_key_stops_the_api_at_startup_naming_the_variable()
    {
        var settings = new Dictionary<string, string?> { ["TAMIZA_MAP_BASEMAP_PROVIDER"] = "google" };

        var failure = await Assert.ThrowsAnyAsync<Exception>(() => MapsHarness.CreateAsync(postgres, settings));

        Assert.Contains("TAMIZA_MAP_GOOGLE_API_KEY", failure.ToString());
    }

    [Fact]
    public async Task Blank_map_variables_as_compose_passes_them_start_the_api_with_no_basemaps()
    {
        var settings = new Dictionary<string, string?>
        {
            ["TAMIZA_MAP_BASEMAP_PROVIDER"] = "",
            ["TAMIZA_MAP_GOOGLE_API_KEY"] = "",
            ["TAMIZA_MAP_GOOGLE_LANGUAGE"] = "",
            ["TAMIZA_MAP_GOOGLE_REGION"] = "",
            ["TAMIZA_MAP_TILES_PER_MINUTE"] = "600",
            ["TAMIZA_MAP_TILES_PER_USER_PER_DAY"] = "3000",
        };
        await using var h = await MapsHarness.CreateAsync(postgres, settings);

        var list = await h.ListAsync();

        Assert.Equal("none", list.GetProperty("provider").GetString());
    }

    [Fact]
    public async Task The_openapi_document_describes_the_basemap_routes()
    {
        await using var h = await MapsHarness.CreateAsync(postgres, MapsHarness.GoogleSettings());

        var document = await h.Anonymous.GetFromJsonAsync<JsonElement>("/api/v1/openapi.json");

        var paths = document.GetProperty("paths").EnumerateObject().Select(p => p.Name).Where(p => p.Contains("basemap")).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(
            ["/api/v1/basemap-tiles/{link}/{z}/{x}/{y}", "/api/v1/basemaps", "/api/v1/basemaps/{kind}/credits"],
            paths);
        var schemas = document.GetProperty("components").GetProperty("schemas");
        var basemap = schemas.GetProperty("Basemap").GetProperty("properties").EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(["attribution", "kind", "linksExpireAt", "linksRefreshInSeconds", "maxZoom", "minZoom", "tiles"], basemap);
        Assert.True(schemas.TryGetProperty("BasemapList", out _));
        var list = document.GetProperty("paths").GetProperty("/api/v1/basemaps").GetProperty("get");
        Assert.Equal("ListBasemaps", list.GetProperty("operationId").GetString());
        Assert.Equal("GetBasemapTile", document.GetProperty("paths").GetProperty("/api/v1/basemap-tiles/{link}/{z}/{x}/{y}").GetProperty("get").GetProperty("operationId").GetString());
        Assert.Equal("GetBasemapCredits", document.GetProperty("paths").GetProperty("/api/v1/basemaps/{kind}/credits").GetProperty("get").GetProperty("operationId").GetString());
    }
}
