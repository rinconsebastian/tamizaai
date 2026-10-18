using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tamiza.Api.IntegrationTests.Infrastructure;
using Tamiza.Api.Maps;

namespace Tamiza.Api.IntegrationTests.Maps;

/// <summary><c>GET /api/v1/basemaps/{kind}/credits</c>: Google's data credits and imagery depth for the area on screen.</summary>
[Collection(PostgresCollection.Name)]
public sealed class BasemapCreditsTests(PostgresFixture postgres)
{
    private const string View = "zoom=14&north=6&south=1&east=6&west=1";

    private static async Task<JsonElement> ProblemAsync(HttpResponseMessage response) =>
        JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());

    [Fact]
    public async Task The_answer_is_the_copyright_text_and_the_depth_of_the_deepest_rectangle_over_the_view()
    {
        await using var h = await MapsHarness.CreateAsync(postgres, MapsHarness.GoogleSettings());
        h.Google.MaxZoomRects =
            """
            [{"maxZoom":19,"north":90,"south":-90,"east":180,"west":-180},
             {"maxZoom":9,"north":90,"south":-90,"east":180,"west":-180},
             {"maxZoom":13,"north":83,"south":-57,"east":180,"west":-180},
             {"maxZoom":20,"north":5,"south":-5,"east":5,"west":-5}]
            """;

        var response = await h.Member.GetAsync($"/api/v1/basemaps/satellite/credits?{View}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ProblemAsync(response);
        Assert.Equal(["maxZoom", "text"], body.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal("Map data ©2026 Google", body.GetProperty("text").GetString());
        Assert.Equal(20, body.GetProperty("maxZoom").GetInt32()); // not 9, which the world-wide rectangle would give
    }

    [Fact]
    public async Task The_credits_follow_the_view_and_the_depth_is_the_deepest_zoom_where_no_rectangle_contains_the_centre()
    {
        await using var h = await MapsHarness.CreateAsync(postgres, MapsHarness.GoogleSettings());
        h.Google.MaxZoomRects = """[{"maxZoom":18,"north":5,"south":0,"east":5,"west":0}]""";

        var near = await ProblemAsync(await h.Member.GetAsync("/api/v1/basemaps/satellite/credits?zoom=10&north=4&south=1&east=4&west=1"));
        var far = await ProblemAsync(await h.Member.GetAsync("/api/v1/basemaps/satellite/credits?zoom=10&north=-20&south=-30&east=-20&west=-30"));

        Assert.Equal(18, near.GetProperty("maxZoom").GetInt32());
        Assert.Equal(22, far.GetProperty("maxZoom").GetInt32());
    }

    [Fact]
    public async Task The_viewport_call_carries_only_the_session_the_key_the_zoom_and_the_four_bounds()
    {
        await using var h = await MapsHarness.CreateAsync(postgres, MapsHarness.GoogleSettings());

        (await h.Member.GetAsync("/api/v1/basemaps/streets/credits?zoom=14&north=6.5&south=1.25&east=7.5&west=-2.75")).EnsureSuccessStatusCode();

        var call = Assert.Single(h.Google.ViewportCalls);
        Assert.Equal(HttpMethod.Get, call.Method);
        Assert.Equal("/tile/v1/viewport", call.Path);
        Assert.Equal(["east", "key", "north", "session", "south", "west", "zoom"], call.Query.Keys.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(("14", "6.5", "1.25", "7.5", "-2.75"), (call.Query["zoom"], call.Query["north"], call.Query["south"], call.Query["east"], call.Query["west"]));
        Assert.Equal(GoogleStub.Key, call.Query["key"]);
        Assert.Null(call.Body);
        Assert.Equal(1, h.Google.SessionsCreated); // the session is created on the first request
        Assert.Contains("mapType", h.Google.SessionCalls[0].Body);
    }

    [Fact]
    public async Task Nothing_in_the_answer_or_the_logs_holds_the_key()
    {
        await using var h = await MapsHarness.CreateAsync(postgres, MapsHarness.GoogleSettings());

        var response = await h.Member.GetAsync($"/api/v1/basemaps/streets/credits?{View}");
        h.Google.Intercept = call => call.Path == "/tile/v1/viewport" ? GoogleStub.Error(HttpStatusCode.InternalServerError, "backendError", GoogleStub.Key, "INTERNAL") : null;
        var failed = await h.Member.GetAsync($"/api/v1/basemaps/streets/credits?{View}");

        foreach (var r in new[] { response, failed })
        {
            Assert.DoesNotContain(GoogleStub.Key, r.Headers + await r.Content.ReadAsStringAsync());
        }

        Assert.DoesNotContain(h.Factory.Logs.Lines, line => line.Contains(GoogleStub.Key));
    }

    [Theory]
    [InlineData("none")]
    [InlineData("osm")]
    public async Task Under_a_provider_that_is_not_google_there_are_no_credits(string provider)
    {
        await using var h = await MapsHarness.CreateAsync(postgres, provider == "none" ? null : MapsHarness.GoogleSettings(provider));

        Assert.Equal(HttpStatusCode.NotFound, (await h.Member.GetAsync($"/api/v1/basemaps/streets/credits?{View}")).StatusCode);
        Assert.Empty(h.Google.Calls);
    }

    [Fact]
    public async Task A_basemap_this_deployment_does_not_have_is_404()
    {
        await using var h = await MapsHarness.CreateAsync(postgres, MapsHarness.GoogleSettings());

        foreach (var kind in new[] { "hybrid", "Streets", "terrain" })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await h.Member.GetAsync($"/api/v1/basemaps/{kind}/credits?{View}")).StatusCode);
        }

        Assert.Empty(h.Google.Calls);
    }

    [Theory]
    [InlineData("zoom=23&north=6&south=1&east=6&west=1", "zoom")]
    [InlineData("zoom=-1&north=6&south=1&east=6&west=1", "zoom")]
    [InlineData("north=6&south=1&east=6&west=1", "zoom")]
    [InlineData("zoom=5&south=1&east=6&west=1", "north")]
    [InlineData("zoom=5&north=91&south=1&east=6&west=1", "north")]
    [InlineData("zoom=5&north=6&south=-91&east=6&west=1", "south")]
    [InlineData("zoom=5&north=6&south=1&east=181&west=1", "east")]
    [InlineData("zoom=5&north=6&south=1&east=6&west=-181", "west")]
    [InlineData("zoom=5&north=6&south=1&east=6", "west")]
    [InlineData("zoom=5&north=1&south=6&east=6&west=1", "south")]
    public async Task A_bad_box_is_a_400_naming_the_field_and_makes_no_call(string query, string field)
    {
        await using var h = await MapsHarness.CreateAsync(postgres, MapsHarness.GoogleSettings());

        var response = await h.Member.GetAsync($"/api/v1/basemaps/streets/credits?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ProblemAsync(response)).GetProperty("errors").TryGetProperty(field, out _));
        Assert.Empty(h.Google.Calls);
    }

    [Theory]
    [InlineData("zoom=abc&north=6&south=1&east=6&west=1")]
    [InlineData("zoom=5&north=six&south=1&east=6&west=1")]
    [InlineData("zoom=5.5&north=6&south=1&east=6&west=1")]
    public async Task A_value_that_is_not_a_number_is_a_400(string query)
    {
        await using var h = await MapsHarness.CreateAsync(postgres, MapsHarness.GoogleSettings());

        Assert.Equal(HttpStatusCode.BadRequest, (await h.Member.GetAsync($"/api/v1/basemaps/streets/credits?{query}")).StatusCode);
        Assert.Empty(h.Google.Calls);
    }

    [Fact]
    public async Task A_box_over_the_antimeridian_is_accepted()
    {
        await using var h = await MapsHarness.CreateAsync(postgres, MapsHarness.GoogleSettings());

        var response = await h.Member.GetAsync("/api/v1/basemaps/streets/credits?zoom=4&north=10&south=-10&east=-170&west=170");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task The_credits_budget_is_separate_from_the_tile_allowance()
    {
        var settings = MapsHarness.GoogleSettings();
        settings["TAMIZA_MAP_TILES_PER_MINUTE"] = "60";
        await using var h = await MapsHarness.CreateAsync(postgres, settings);
        var tile = MapsHarness.TilePath(MapsHarness.TemplateOf(await h.ListAsync(), "streets"));
        for (var i = 0; i < 60; i++)
        {
            (await h.Anonymous.GetAsync(tile)).EnsureSuccessStatusCode();
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await h.Anonymous.GetAsync(tile)).StatusCode);
        var credits = await h.Member.GetAsync($"/api/v1/basemaps/streets/credits?{View}");

        Assert.Equal(HttpStatusCode.OK, credits.StatusCode);
    }

    [Fact]
    public async Task Credits_over_their_own_budget_are_429_with_retry_after_and_tiles_keep_flowing()
    {
        await using var h = await MapsHarness.CreateAsync(postgres, MapsHarness.GoogleSettings(),
            services => services.PostConfigure<MapOptions>(options => options.CreditsPerMinute = 5));
        var tile = MapsHarness.TilePath(MapsHarness.TemplateOf(await h.ListAsync(), "streets"));

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await h.Member.GetAsync($"/api/v1/basemaps/streets/credits?{View}")).StatusCode);
        }

        var refused = await h.Member.GetAsync($"/api/v1/basemaps/streets/credits?{View}");

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.InRange(int.Parse(Assert.Single(refused.Headers.GetValues("Retry-After"))), 1, 60);
        Assert.Equal("basemaps.rateLimited", (await ProblemAsync(refused)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.OK, (await h.Anonymous.GetAsync(tile)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await h.OtherMember.GetAsync($"/api/v1/basemaps/streets/credits?{View}")).StatusCode);
        Assert.Equal(6, h.Google.ViewportCalls.Count); // five of the first member and one of the other; the refused one never reached Google
    }

    [Fact]
    public async Task A_session_google_rejected_is_replaced_once_for_credits_too()
    {
        await using var h = await MapsHarness.CreateAsync(postgres, MapsHarness.GoogleSettings());
        (await h.Member.GetAsync($"/api/v1/basemaps/streets/credits?{View}")).EnsureSuccessStatusCode();
        h.Google.Reject("session-1");

        var response = await h.Member.GetAsync($"/api/v1/basemaps/streets/credits?{View}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, h.Google.SessionsCreated);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, "quotaExceeded", HttpStatusCode.ServiceUnavailable, "basemaps.unavailable")]
    [InlineData(HttpStatusCode.InternalServerError, "backendError", HttpStatusCode.BadGateway, "basemaps.upstream")]
    public async Task Google_failures_are_503_or_502_and_googles_words_are_not_passed_on(HttpStatusCode google, string reason, HttpStatusCode expected, string code)
    {
        await using var h = await MapsHarness.CreateAsync(postgres, MapsHarness.GoogleSettings());
        h.Google.Intercept = call => call.Path == "/tile/v1/viewport" ? GoogleStub.Error(google, reason, "words-of-google", "X") : null;

        var response = await h.Member.GetAsync($"/api/v1/basemaps/streets/credits?{View}");

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(code, (await ProblemAsync(response)).GetProperty("code").GetString());
        Assert.DoesNotContain("words-of-google", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_user_who_belongs_to_no_project_can_read_the_credits_which_are_not_a_link_or_a_tile()
    {
        await using var h = await MapsHarness.CreateAsync(postgres, MapsHarness.GoogleSettings());

        Assert.Equal(HttpStatusCode.OK, (await h.Outsider.GetAsync($"/api/v1/basemaps/streets/credits?{View}")).StatusCode);
    }
}
