using System.Net;
using Tamiza.Api.Maps;

namespace Tamiza.Api.UnitTests.Maps;

public sealed class GoogleTilesTests
{
    private static string[] SessionsAndTiles(ScriptedHandler google) =>
        google.Requests.Select(r => r.Uri.AbsolutePath).ToArray();

    /// <summary>A Google whose sessions last two weeks and whose other answers come from <paramref name="other"/>.</summary>
    private static ScriptedHandler Google(Func<HttpRequestMessage, HttpResponseMessage> other)
    {
        var count = 0;
        return new ScriptedHandler((request, _) =>
            request.RequestUri!.AbsolutePath == "/v1/createSession"
                ? ScriptedHandler.Session($"session-{Interlocked.Increment(ref count)}", (MapsServices.DefaultStart + TimeSpan.FromDays(14)).ToUnixTimeSeconds())
                : other(request));
    }

    // Tiles

    [Fact]
    public async Task A_tile_is_asked_for_by_coordinates_with_only_the_session_and_the_key()
    {
        var google = Google(_ => ScriptedHandler.Png());
        using var services = new MapsServices(google);

        var tile = await services.Tiles.GetTileAsync("streets", 12, 1200, 1900, null, CancellationToken.None);

        Assert.Equal(GoogleOutcome.Ok, tile.Outcome);
        var request = google.Requests.Single(r => r.Uri.AbsolutePath != "/v1/createSession");
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/v1/2dtiles/12/1200/1900", request.Uri.AbsolutePath);
        Assert.Equal($"?session=session-1&key={MapsServices.Key}", request.Uri.Query);
        Assert.Null(request.Body);
    }

    [Fact]
    public async Task A_tile_comes_with_its_content_type_and_cache_rules_and_a_304_is_passed_on_with_the_browsers_etag_forwarded()
    {
        string? forwarded = null;
        var google = Google(request =>
        {
            forwarded = request.Headers.TryGetValues("If-None-Match", out var values) ? string.Join(",", values) : null;
            var response = forwarded is null ? ScriptedHandler.Png([1, 2, 3]) : new HttpResponseMessage(HttpStatusCode.NotModified);
            response.Headers.TryAddWithoutValidation("ETag", "\"abc\"");
            response.Headers.TryAddWithoutValidation("Cache-Control", "private, max-age=86400");
            return response;
        });
        using var services = new MapsServices(google);

        var fresh = await services.Tiles.GetTileAsync("streets", 1, 0, 0, null, CancellationToken.None);
        var revalidated = await services.Tiles.GetTileAsync("streets", 1, 0, 0, "\"abc\"", CancellationToken.None);

        Assert.Equal(GoogleOutcome.Ok, fresh.Outcome);
        Assert.Equal([1, 2, 3], fresh.Body);
        Assert.Equal("image/png", fresh.ContentType);
        Assert.Equal("private, max-age=86400", fresh.CacheControl);
        Assert.Equal("\"abc\"", fresh.ETag);
        Assert.Equal(GoogleOutcome.NotModified, revalidated.Outcome);
        Assert.Equal("\"abc\"", forwarded);
        Assert.Null(revalidated.Body);
    }

    [Fact]
    public async Task A_session_rejected_as_expired_is_replaced_and_the_tile_asked_for_again_once()
    {
        var google = Google(request => request.RequestUri!.Query.Contains("session=session-1")
            ? ScriptedHandler.Error(HttpStatusCode.BadRequest, "expired", "The session token has expired.")
            : ScriptedHandler.Png());
        using var services = new MapsServices(google);

        var tile = await services.Tiles.GetTileAsync("streets", 3, 1, 1, null, CancellationToken.None);

        Assert.Equal(GoogleOutcome.Ok, tile.Outcome);
        Assert.Equal(["/v1/createSession", "/v1/2dtiles/3/1/1", "/v1/createSession", "/v1/2dtiles/3/1/1"], SessionsAndTiles(google));
        Assert.Contains("session=session-2", google.Requests[3].Uri.Query);
    }

    [Fact]
    public async Task Two_rejections_in_a_row_are_upstream_with_no_third_try()
    {
        var google = Google(_ => ScriptedHandler.Error(HttpStatusCode.BadRequest, "expired", "The session token has expired."));
        using var services = new MapsServices(google);

        var tile = await services.Tiles.GetTileAsync("streets", 3, 1, 1, null, CancellationToken.None);

        Assert.Equal(GoogleOutcome.Upstream, tile.Outcome);
        Assert.Equal(4, google.Requests.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "notFound", GoogleOutcome.Missing)]
    [InlineData(HttpStatusCode.BadRequest, "notFound", GoogleOutcome.Missing)]
    [InlineData(HttpStatusCode.BadRequest, "invalid", GoogleOutcome.Missing)]
    [InlineData(HttpStatusCode.TooManyRequests, "quotaExceeded", GoogleOutcome.Unavailable)]
    [InlineData(HttpStatusCode.TooManyRequests, "rateLimitExceeded", GoogleOutcome.Unavailable)]
    [InlineData(HttpStatusCode.Forbidden, "forbidden", GoogleOutcome.Unavailable)]
    [InlineData(HttpStatusCode.InternalServerError, "backendError", GoogleOutcome.Upstream)]
    [InlineData(HttpStatusCode.BadGateway, "backendError", GoogleOutcome.Upstream)]
    [InlineData(HttpStatusCode.BadRequest, "badRequest", GoogleOutcome.Upstream)]
    public async Task Google_errors_are_told_apart_by_their_documented_reason_and_never_pass_on_its_words(HttpStatusCode status, string reason, GoogleOutcome expected)
    {
        var google = Google(_ => ScriptedHandler.Error(status, reason, "words of google nobody should see"));
        using var services = new MapsServices(google);

        var tile = await services.Tiles.GetTileAsync("streets", 3, 1, 1, null, CancellationToken.None);

        Assert.Equal(expected, tile.Outcome);
        Assert.Null(tile.Body);
        Assert.DoesNotContain(services.Logs.Lines, l => l.Contains("words of google nobody should see"));
    }

    [Fact]
    public async Task An_invalid_reason_that_names_the_key_or_the_session_is_not_a_missing_tile()
    {
        var google = Google(_ => ScriptedHandler.Error(HttpStatusCode.BadRequest, "invalid", "API key not valid."));
        using var services = new MapsServices(google);

        var tile = await services.Tiles.GetTileAsync("streets", 3, 1, 1, null, CancellationToken.None);

        Assert.Equal(GoogleOutcome.Upstream, tile.Outcome);
    }

    [Fact]
    public async Task An_error_that_is_not_json_is_told_by_its_status_alone()
    {
        var google = Google(_ => new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("<html>Not Found</html>") });
        using var services = new MapsServices(google);

        Assert.Equal(GoogleOutcome.Missing, (await services.Tiles.GetTileAsync("streets", 3, 1, 1, null, CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task An_answer_that_is_not_an_image_is_upstream()
    {
        var google = Google(_ => ScriptedHandler.Json("""{"hello":"world"}"""));
        using var services = new MapsServices(google);

        Assert.Equal(GoogleOutcome.Upstream, (await services.Tiles.GetTileAsync("streets", 3, 1, 1, null, CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task A_tile_beyond_the_size_limit_is_upstream()
    {
        var google = Google(_ => ScriptedHandler.Png(new byte[3 * 1024 * 1024]));
        using var services = new MapsServices(google);

        Assert.Equal(GoogleOutcome.Upstream, (await services.Tiles.GetTileAsync("streets", 3, 1, 1, null, CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task A_redirect_is_never_followed()
    {
        var google = Google(_ => new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("https://elsewhere.example/tile") } });
        using var services = new MapsServices(google);

        var tile = await services.Tiles.GetTileAsync("streets", 3, 1, 1, null, CancellationToken.None);

        Assert.Equal(GoogleOutcome.Upstream, tile.Outcome);
        Assert.DoesNotContain(google.Requests, r => r.Uri.Host == "elsewhere.example");
    }

    [Fact]
    public async Task An_unreachable_google_is_upstream()
    {
        var google = Google(_ => throw new HttpRequestException("no route"));
        using var services = new MapsServices(google);

        Assert.Equal(GoogleOutcome.Upstream, (await services.Tiles.GetTileAsync("streets", 3, 1, 1, null, CancellationToken.None)).Outcome);
    }

    // Credits

    // Shaped like Google's real answers: the world-wide rectangles come first (19 and 9), then ever smaller deeper ones.
    private static readonly string Rects = """
        [{"maxZoom":19,"north":90,"south":-90,"east":180,"west":-180},
         {"maxZoom":9,"north":90,"south":-90,"east":180,"west":-180},
         {"maxZoom":13,"north":83,"south":-57,"east":180,"west":-180},
         {"maxZoom":20,"north":5,"south":-5,"east":5,"west":-5}]
        """;

    private static ScriptedHandler Viewport(string copyright, string rects) =>
        Google(_ => ScriptedHandler.Json($$"""{"copyright":"{{copyright}}","maxZoomRects":{{rects}}}"""));

    [Fact]
    public async Task The_viewport_call_carries_only_the_session_the_key_the_zoom_and_the_four_bounds()
    {
        var google = Viewport("Map data", Rects);
        using var services = new MapsServices(google);

        await services.Tiles.GetCreditsAsync("satellite", 14, 6.5, 1.25, 7.5, 2.75, CancellationToken.None);

        var request = google.Requests.Single(r => r.Uri.AbsolutePath == "/tile/v1/viewport");
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(
            $"?session=session-1&key={MapsServices.Key}&zoom=14&north=6.5&south=1.25&east=7.5&west=2.75",
            request.Uri.Query);
        Assert.Null(request.Body);
    }

    [Fact]
    public async Task The_credits_are_the_copyright_and_the_deepest_rectangle_over_the_centre_of_the_box()
    {
        using var services = new MapsServices(Viewport("Map data ©2026 Google", Rects));

        var credits = await services.Tiles.GetCreditsAsync("satellite", 14, 6, 1, 6, 1, CancellationToken.None);

        Assert.Equal(GoogleOutcome.Ok, credits.Outcome);
        Assert.Equal("Map data ©2026 Google", credits.Text);
        Assert.Equal(20, credits.MaxZoom); // not 9, the smallest, which the world-wide rectangle would give anywhere
    }

    [Theory]
    [InlineData(-1, -3, -1, -3, 20)] // inside the deepest rectangle
    [InlineData(55, 52, 55, 52, 19)] // the world-wide 19, the 13 and the 9 cover it: the deepest wins
    [InlineData(-70, -80, -70, -80, 19)] // beyond latitude -57: the world-wide 19 and 9 only
    public async Task The_depth_is_that_of_the_deepest_rectangle_that_contains_the_centre(double north, double south, double east, double west, int expected)
    {
        using var services = new MapsServices(Viewport("c", Rects));

        var credits = await services.Tiles.GetCreditsAsync("satellite", 10, north, south, east, west, CancellationToken.None);

        Assert.Equal(expected, credits.MaxZoom);
    }

    [Fact]
    public async Task A_centre_that_no_rectangle_contains_has_no_depth_limit()
    {
        using var services = new MapsServices(Viewport("c", """[{"maxZoom":18,"north":5,"south":0,"east":5,"west":0}]"""));

        var credits = await services.Tiles.GetCreditsAsync("satellite", 10, -20, -30, -20, -30, CancellationToken.None);

        Assert.Equal(22, credits.MaxZoom);
    }

    [Fact]
    public async Task A_box_that_crosses_the_antimeridian_has_its_centre_on_the_right_side()
    {
        const string rects = """
            [{"maxZoom":9,"north":10,"south":-10,"east":180,"west":175},
             {"maxZoom":11,"north":10,"south":-10,"east":-170,"west":-180},
             {"maxZoom":15,"north":10,"south":-10,"east":-170,"west":170}]
            """;
        using var services = new MapsServices(Viewport("c", rects));

        var across = await services.Tiles.GetCreditsAsync("satellite", 6, 5, -5, -172, 177, CancellationToken.None); // centre at -177.5
        var west = await services.Tiles.GetCreditsAsync("satellite", 6, 5, -5, 179, 176, CancellationToken.None); // centre at 177.5

        Assert.Equal(15, across.MaxZoom); // the rectangle that itself crosses the antimeridian contains both centres
        Assert.Equal(15, west.MaxZoom);
    }

    [Fact]
    public async Task Credits_without_rectangles_have_no_depth_limit_and_missing_copyright_is_empty_text()
    {
        using var services = new MapsServices(Google(_ => ScriptedHandler.Json("{}")));

        var credits = await services.Tiles.GetCreditsAsync("streets", 3, 1, 0, 1, 0, CancellationToken.None);

        Assert.Equal((GoogleOutcome.Ok, "", 22), (credits.Outcome, credits.Text, credits.MaxZoom));
    }

    [Fact]
    public async Task A_credits_failure_is_unavailable_for_a_refusal_and_upstream_otherwise()
    {
        using var refused = new MapsServices(Google(_ => ScriptedHandler.Error(HttpStatusCode.TooManyRequests, "quotaExceeded", "q")));
        using var broken = new MapsServices(Google(_ => ScriptedHandler.Json("not json at all")));

        Assert.Equal(GoogleOutcome.Unavailable, (await refused.Tiles.GetCreditsAsync("streets", 3, 1, 0, 1, 0, CancellationToken.None)).Outcome);
        Assert.Equal(GoogleOutcome.Upstream, (await broken.Tiles.GetCreditsAsync("streets", 3, 1, 0, 1, 0, CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task Tiles_and_credits_of_one_basemap_share_its_session()
    {
        var google = Google(request => request.RequestUri!.AbsolutePath.StartsWith("/v1/2dtiles") ? ScriptedHandler.Png() : ScriptedHandler.Json("""{"copyright":"c","maxZoomRects":[]}"""));
        using var services = new MapsServices(google);

        await services.Tiles.GetTileAsync("streets", 1, 0, 0, null, CancellationToken.None);
        await services.Tiles.GetCreditsAsync("streets", 1, 1, 0, 1, 0, CancellationToken.None);
        await services.Tiles.GetTileAsync("satellite", 1, 0, 0, null, CancellationToken.None);

        Assert.Equal(2, google.Requests.Count(r => r.Uri.AbsolutePath == "/v1/createSession"));
    }

    // 6.1: the key in the logs

    [Fact]
    public async Task No_log_line_of_a_relayed_tile_a_failed_tile_or_a_session_holds_the_key()
    {
        var mode = "ok";
        var google = new ScriptedHandler((request, _) => request.RequestUri!.AbsolutePath switch
        {
            "/v1/createSession" => mode == "session" ? ScriptedHandler.Error(HttpStatusCode.Forbidden, "forbidden", "key refused") : ScriptedHandler.Session("session-1", (MapsServices.DefaultStart + TimeSpan.FromDays(14)).ToUnixTimeSeconds()),
            _ => mode == "ok" ? ScriptedHandler.Png() : ScriptedHandler.Error(HttpStatusCode.InternalServerError, "backendError", "boom"),
        });
        using var services = new MapsServices(google);

        await services.Tiles.GetTileAsync("streets", 1, 0, 0, null, CancellationToken.None); // relayed
        mode = "fail";
        await services.Tiles.GetTileAsync("streets", 1, 0, 1, null, CancellationToken.None); // failed
        await services.Tiles.GetCreditsAsync("streets", 1, 1, 0, 1, 0, CancellationToken.None); // failed credits
        mode = "session";
        await services.Tiles.GetTileAsync("satellite", 1, 0, 0, null, CancellationToken.None); // failed session creation

        Assert.NotEmpty(services.Logs.Lines); // the failures are logged...
        Assert.DoesNotContain(services.Logs.Lines, line => line.Contains(MapsServices.Key)); // ...without the key
        Assert.DoesNotContain(services.Logs.Lines, line => line.Contains("googleapis"));
        Assert.DoesNotContain(services.Logs.Lines, line => line.Contains("session-1"));
    }

    [Fact]
    public async Task The_factorys_own_logging_writes_the_request_so_the_test_above_would_see_it_if_it_were_not_removed()
    {
        var google = Google(_ => ScriptedHandler.Png());
        using var services = new MapsServices(google);

        using var response = await services.Control.GetAsync($"https://tile.googleapis.com/v1/2dtiles/1/0/0?session=s&key={MapsServices.Key}");

        // The control client logs the request it sends (the address, at least); the real one logs none of that.
        Assert.Contains(services.Logs.Lines, line => line.Contains("tile.googleapis.com"));
    }

    [Fact]
    public async Task A_failure_is_logged_once_a_minute_per_basemap()
    {
        var google = Google(_ => ScriptedHandler.Error(HttpStatusCode.InternalServerError, "backendError", "boom"));
        using var services = new MapsServices(google);

        for (var i = 0; i < 5; i++)
        {
            await services.Tiles.GetTileAsync("streets", 1, 0, i % 2, null, CancellationToken.None);
        }

        Assert.Single(services.Logs.Lines, l => l.Contains("Google Map Tiles") && l.Contains("streets"));
        services.Time.Advance(TimeSpan.FromSeconds(61));
        await services.Tiles.GetTileAsync("streets", 1, 0, 0, null, CancellationToken.None);
        Assert.Equal(2, services.Logs.Lines.Count(l => l.Contains("Google Map Tiles") && l.Contains("streets")));
    }
}
