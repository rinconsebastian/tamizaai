using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.Maps;

/// <summary><c>GET /api/v1/basemap-tiles/{link}/{z}/{x}/{y}</c>: Google's tiles relayed through a link, with a fake Google.</summary>
[Collection(PostgresCollection.Name)]
public sealed class BasemapTileTests(PostgresFixture postgres)
{
    private static async Task<string> TileUrlAsync(MapsHarness h, string kind = "streets", HttpClient? client = null, string z = "3", string x = "2", string y = "4") =>
        MapsHarness.TilePath(MapsHarness.TemplateOf(await h.ListAsync(client), kind), z, x, y);

    private static async Task<JsonElement> ProblemAsync(HttpResponseMessage response) =>
        JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());

    // The relay

    [Fact]
    public async Task A_tile_is_served_without_a_token_with_googles_bytes_and_cache_rules_and_only_for_this_deployments_pages()
    {
        await using var h = await MapsHarness.CreateAsync(postgres, MapsHarness.GoogleSettings());

        var response = await h.Anonymous.GetAsync(await TileUrlAsync(h));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(GoogleStub.TileBytes, await response.Content.ReadAsByteArrayAsync());
        Assert.Equal("image/png", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(GoogleStub.TileCacheControl, response.Headers.NonValidated["Cache-Control"].ToString()); // as written, not reordered
        Assert.Equal(GoogleStub.TileETag, response.Headers.ETag!.ToString());
        Assert.Equal("same-origin", Assert.Single(response.Headers.GetValues("Cross-Origin-Resource-Policy")));
    }

    [Fact]
    public async Task Google_receives_only_a_tile_request_with_the_session_and_the_key_and_a_session_request_with_the_map_type()
    {
        var settings = MapsHarness.GoogleSettings();
        settings["TAMIZA_MAP_GOOGLE_LANGUAGE"] = "es-419";
        settings["TAMIZA_MAP_GOOGLE_REGION"] = "co";
        await using var h = await MapsHarness.CreateAsync(postgres, settings);

        (await h.Anonymous.GetAsync(await TileUrlAsync(h, "streets", z: "12", x: "1200", y: "1900"))).EnsureSuccessStatusCode();
        (await h.Anonymous.GetAsync(await TileUrlAsync(h, "satellite", z: "5", x: "10", y: "11"))).EnsureSuccessStatusCode();

        Assert.Equal(2, h.Google.SessionCalls.Count);
        Assert.All(h.Google.SessionCalls, c => Assert.Equal(HttpMethod.Post, c.Method));
        var bodies = h.Google.SessionCalls.Select(c => JsonSerializer.Deserialize<JsonElement>(c.Body!)).ToArray();
        Assert.Equal(["roadmap", "satellite"], bodies.Select(b => b.GetProperty("mapType").GetString()).ToArray());
        Assert.All(bodies, b => Assert.Equal(("es-419", "CO"), (b.GetProperty("language").GetString(), b.GetProperty("region").GetString())));
        Assert.All(h.Google.SessionCalls, c => Assert.Equal([GoogleStub.Key], c.Query.Values.ToArray()));

        Assert.Equal(2, h.Google.TileCalls.Count);
        var tile = h.Google.TileCalls[0];
        Assert.Equal(HttpMethod.Get, tile.Method);
        Assert.Equal("/v1/2dtiles/12/1200/1900", tile.Path);
        Assert.Equal(["key", "session"], tile.Query.Keys.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(GoogleStub.Key, tile.Query["key"]);
        Assert.Null(tile.Body);
        Assert.False(tile.Headers.ContainsKey("If-None-Match"));
        Assert.Equal("/v1/2dtiles/5/10/11", h.Google.TileCalls[1].Path);
        Assert.NotEqual(tile.Query["session"], h.Google.TileCalls[1].Query["session"]);
        Assert.Equal(4, h.Google.Calls.Count); // two sessions, two tiles, nothing else
    }

    [Fact]
    public async Task The_key_is_in_no_response_the_browser_gets()
    {
        await using var h = await MapsHarness.CreateAsync(postgres, MapsHarness.GoogleSettings());
        var list = await h.ListAsync();
        var url = MapsHarness.TilePath(MapsHarness.TemplateOf(list, "satellite"));

        using var ok = await h.Anonymous.GetAsync(url);
        h.Google.Intercept = _ => GoogleStub.Error(HttpStatusCode.Forbidden, "forbidden", $"The request is missing a valid API key {GoogleStub.Key}.", "PERMISSION_DENIED");
        using var refused = await h.Anonymous.GetAsync(MapsHarness.TilePath(MapsHarness.TemplateOf(list, "streets")));

        foreach (var response in new[] { ok, refused })
        {
            Assert.DoesNotContain(GoogleStub.Key, response.Headers.ToString() + response.Content.Headers + await response.Content.ReadAsStringAsync());
        }

        Assert.DoesNotContain(GoogleStub.Key, list.GetRawText());
    }

    [Fact]
    public async Task No_log_line_of_the_api_holds_the_key()
    {
        await using var h = await MapsHarness.CreateAsync(postgres, MapsHarness.GoogleSettings());
        var url = await TileUrlAsync(h);
        (await h.Anonymous.GetAsync(url)).EnsureSuccessStatusCode();
        h.Google.Intercept = _ => GoogleStub.Error(HttpStatusCode.InternalServerError, "backendError", "boom", "INTERNAL");
        (await h.Anonymous.GetAsync(await TileUrlAsync(h, "satellite"))).Dispose();

        Assert.DoesNotContain(h.Factory.Logs.Lines, line => line.Contains(GoogleStub.Key));
        Assert.Contains(h.Factory.Logs.Lines, line => line.Contains("Map basemaps: provider google"));
    }

    [Fact]
    public async Task A_tile_the_browser_already_has_is_a_304_with_google_asked_to_revalidate()
    {
        await using var h = await MapsHarness.CreateAsync(postgres, MapsHarness.GoogleSettings());
        var url = await TileUrlAsync(h);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("If-None-Match", GoogleStub.TileETag);

        var response = await h.Anonymous.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotModified, response.StatusCode);
        Assert.Equal(GoogleStub.TileETag, response.Headers.ETag!.ToString());
        Assert.Equal(GoogleStub.TileETag, h.Google.TileCalls.Single().Headers["If-None-Match"]);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Relaying_tiles_writes_no_file_under_the_apis_data_paths()
    {
        await using var h = await MapsHarness.CreateAsync(postgres, MapsHarness.GoogleSettings());
        var url = await TileUrlAsync(h);
        (await h.Anonymous.GetAsync(url)).EnsureSuccessStatusCode(); // the key ring exists after the first link
        var before = Snapshot(h.Factory.KeysPath);

        for (var i = 0; i < 5; i++)
        {
            (await h.Anonymous.GetAsync(url.Replace("/4", "/" + i))).EnsureSuccessStatusCode();
        }

        Assert.Equal(before, Snapshot(h.Factory.KeysPath));
    }

    private static string[] Snapshot(string directory) =>
        Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Select(f => $"{f}|{new FileInfo(f).Length}").Order(StringComparer.Ordinal).ToArray();

    // Checks before any call to Google

    [Fact]
    public async Task A_link_the_api_did_not_issue_is_404_without_a_call_to_google()
    {
        await using var h = await MapsHarness.CreateAsync(postgres, MapsHarness.GoogleSettings());
        var url = await TileUrlAsync(h);
        var link = MapsHarness.LinkOf(MapsHarness.TemplateOf(await h.ListAsync(), "streets"));
        var tampered = link[..12] + (link[12] == 'A' ? 'B' : 'A') + link[13..];
        var otherPurpose = h.Factory.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("Tamiza.Maps.TileLink.v0")
            .Protect($"{Guid.NewGuid():N}|streets|{h.Time.GetUtcNow().ToUnixTimeSeconds()}");

        foreach (var forged in new[] { "not-a-link", tampered, otherPurpose, new string('A', 150), link[..^3] })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await h.Anonymous.GetAsync(url.Replace(link, forged))).StatusCode);
        }

        Assert.Empty(h.Google.Calls);
    }

    [Fact]
    public async Task A_link_issued_more_than_an_hour_ago_is_404_without_a_call_to_google()
    {
        await using var h = await MapsHarness.CreateAsync(postgres, MapsHarness.GoogleSettings());
        var url = await TileUrlAsync(h);
        Assert.Equal(HttpStatusCode.OK, (await h.Anonymous.GetAsync(url)).StatusCode);
        var calls = h.Google.Calls.Count;

        h.Time.Advance(TimeSpan.FromMinutes(61));

        Assert.Equal(HttpStatusCode.NotFound, (await h.Anonymous.GetAsync(url)).StatusCode);
        Assert.Equal(calls, h.Google.Calls.Count);
    }

    [Fact]
    public async Task A_link_still_works_in_the_grace_after_its_window_and_a_fresh_list_gives_the_next_one()
    {
        await using var h = await MapsHarness.CreateAsync(postgres, MapsHarness.GoogleSettings());
        var url = await TileUrlAsync(h);

        h.Time.Advance(TimeSpan.FromMinutes(40 + 10)); // ten minutes into the grace: a late timer, a device asleep, a clock that is off

        Assert.Equal(HttpStatusCode.OK, (await h.Anonymous.GetAsync(url)).StatusCode);
        Assert.NotEqual(url, await TileUrlAsync(h));
    }

    [Theory]
    [InlineData("2", "5", "0")] // x past 2^2 - 1
    [InlineData("2", "0", "4")]
    [InlineData("0", "1", "0")]
    [InlineData("23", "0", "0")] // past zoom 22
    [InlineData("-1", "0", "0")]
    [InlineData("1.5", "0", "0")]
    [InlineData("1e1", "0", "0")]
    [InlineData("abc", "0", "0")]
    [InlineData("2", "-1", "0")]
    [InlineData("2", "+1", "0")]
    [InlineData("2", "1", "99999999999999999999")]
    [InlineData("2", " 1", "0")]
    public async Task Coordinates_out_of_range_or_not_whole_numbers_are_404_without_a_call_to_google(string z, string x, string y)
    {
        await using var h = await MapsHarness.CreateAsync(postgres, MapsHarness.GoogleSettings());

        var response = await h.Anonymous.GetAsync(await TileUrlAsync(h, z: z, x: Uri.EscapeDataString(x), y: y));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(h.Google.Calls);
    }

    [Theory]
    [InlineData("0", "0", "0")]
    [InlineData("22", "4194303", "4194303")]
    public async Task The_corners_of_the_map_are_in_range(string z, string x, string y)
    {
        await using var h = await MapsHarness.CreateAsync(postgres, MapsHarness.GoogleSettings());

        Assert.Equal(HttpStatusCode.OK, (await h.Anonymous.GetAsync(await TileUrlAsync(h, z: z, x: x, y: y))).StatusCode);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("osm")]
    public async Task When_the_provider_is_not_google_a_tile_is_404_and_no_session_or_call_is_made(string provider)
    {
        await using var google = await MapsHarness.CreateAsync(postgres, MapsHarness.GoogleSettings());
        var link = MapsHarness.LinkOf(MapsHarness.TemplateOf(await google.ListAsync(), "streets"));
        await using var h = await MapsHarness.CreateAsync(postgres, provider == "none" ? null : MapsHarness.GoogleSettings(provider));

        var response = await h.Anonymous.GetAsync($"/api/v1/basemap-tiles/{link}/3/2/4");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(h.Google.Calls);
    }

    // Google's side

    [Fact]
    public async Task A_session_google_rejected_is_replaced_and_the_tile_comes_after_one_retry()
    {
        await using var h = await MapsHarness.CreateAsync(postgres, MapsHarness.GoogleSettings());
        var url = await TileUrlAsync(h);
        (await h.Anonymous.GetAsync(url)).EnsureSuccessStatusCode();
        h.Google.Reject("session-1");

        var response = await h.Anonymous.GetAsync(url.Replace("/4", "/5"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, h.Google.SessionsCreated);
        Assert.Equal(["session-1", "session-1", "session-2"], h.Google.TileCalls.Select(c => c.Query["session"]).ToArray());
    }

    [Fact]
    public async Task Two_rejections_in_a_row_are_a_502_with_no_further_retry()
    {
        await using var h = await MapsHarness.CreateAsync(postgres, MapsHarness.GoogleSettings());
        h.Google.Intercept = call => call.Path.StartsWith("/v1/2dtiles/")
            ? GoogleStub.Error(HttpStatusCode.BadRequest, "expired", "The session token has expired. words-of-google", "INVALID_ARGUMENT")
            : null;

        var response = await h.Anonymous.GetAsync(await TileUrlAsync(h));

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("basemaps.upstream", (await ProblemAsync(response)).GetProperty("code").GetString());
        Assert.Equal(2, h.Google.TileCalls.Count);
        Assert.Equal(2, h.Google.SessionsCreated);
    }

    [Fact]
    public async Task A_tile_google_does_not_have_is_404_and_googles_words_are_not_passed_on()
    {
        await using var h = await MapsHarness.CreateAsync(postgres, MapsHarness.GoogleSettings());
        h.Google.Intercept = call => call.Path.StartsWith("/v1/2dtiles/")
            ? GoogleStub.Error(HttpStatusCode.NotFound, "notFound", "Requested entity was not found. words-of-google", "NOT_FOUND")
            : null;

        var response = await h.Anonymous.GetAsync(await TileUrlAsync(h));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("words-of-google", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, "quotaExceeded")]
    [InlineData(HttpStatusCode.TooManyRequests, "rateLimitExceeded")]
    [InlineData(HttpStatusCode.Forbidden, "forbidden")]
    public async Task When_google_refuses_for_quota_or_billing_the_answer_is_503_and_googles_words_are_not_passed_on(HttpStatusCode status, string reason)
    {
        await using var h = await MapsHarness.CreateAsync(postgres, MapsHarness.GoogleSettings());
        h.Google.Intercept = call => call.Path.StartsWith("/v1/2dtiles/") ? GoogleStub.Error(status, reason, "Quota exceeded for project 12345. words-of-google", "RESOURCE_EXHAUSTED") : null;

        var response = await h.Anonymous.GetAsync(await TileUrlAsync(h));
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("basemaps.unavailable", JsonSerializer.Deserialize<JsonElement>(text).GetProperty("code").GetString());
        Assert.DoesNotContain("words-of-google", text);
        Assert.DoesNotContain("12345", text);
    }

    [Fact]
    public async Task A_quota_refusal_when_the_session_is_created_is_also_503()
    {
        await using var h = await MapsHarness.CreateAsync(postgres, MapsHarness.GoogleSettings());
        h.Google.Intercept = call => call.Path == "/v1/createSession" ? GoogleStub.Error(HttpStatusCode.Forbidden, "forbidden", "billing is off", "PERMISSION_DENIED") : null;

        var response = await h.Anonymous.GetAsync(await TileUrlAsync(h));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Empty(h.Google.TileCalls);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Any_other_google_failure_is_a_502(HttpStatusCode status)
    {
        await using var h = await MapsHarness.CreateAsync(postgres, MapsHarness.GoogleSettings());
        h.Google.Intercept = call => call.Path.StartsWith("/v1/2dtiles/") ? GoogleStub.Error(status, "backendError", "words-of-google", "INTERNAL") : null;

        var response = await h.Anonymous.GetAsync(await TileUrlAsync(h));

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("basemaps.upstream", (await ProblemAsync(response)).GetProperty("code").GetString());
        Assert.DoesNotContain("words-of-google", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_unreachable_google_is_a_502()
    {
        await using var h = await MapsHarness.CreateAsync(postgres, MapsHarness.GoogleSettings());
        h.Google.Intercept = _ => throw new HttpRequestException("no route to host");

        var response = await h.Anonymous.GetAsync(await TileUrlAsync(h));

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    // Limits

    [Fact]
    public async Task The_next_tile_after_the_minute_allowance_is_429_with_retry_after_and_another_user_is_served()
    {
        var settings = MapsHarness.GoogleSettings();
        settings["TAMIZA_MAP_TILES_PER_MINUTE"] = "60";
        await using var h = await MapsHarness.CreateAsync(postgres, settings);
        var url = await TileUrlAsync(h);
        var other = await TileUrlAsync(h, client: h.OtherMember);

        for (var i = 0; i < 60; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await h.Anonymous.GetAsync(url)).StatusCode);
        }

        var refused = await h.Anonymous.GetAsync(url);

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        var retry = int.Parse(Assert.Single(refused.Headers.GetValues("Retry-After")));
        Assert.InRange(retry, 1, 60);
        Assert.Equal("basemaps.rateLimited", (await ProblemAsync(refused)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.OK, (await h.Anonymous.GetAsync(other)).StatusCode);
        Assert.Equal(60 + 1, h.Google.TileCalls.Count); // the refused request never reached Google
    }

    [Fact]
    public async Task A_user_who_used_the_daily_allowance_gets_429_until_midnight_utc_and_another_member_is_served()
    {
        var settings = MapsHarness.GoogleSettings();
        settings["TAMIZA_MAP_TILES_PER_MINUTE"] = "10000";
        settings["TAMIZA_MAP_TILES_PER_USER_PER_DAY"] = "100";
        await using var h = await MapsHarness.CreateAsync(postgres, settings);
        var url = await TileUrlAsync(h);
        var other = await TileUrlAsync(h, client: h.OtherMember);

        for (var i = 0; i < 100; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await h.Anonymous.GetAsync(url)).StatusCode);
        }

        var refused = await h.Anonymous.GetAsync(url);
        var midnight = h.Time.GetUtcNow().UtcDateTime.Date.AddDays(1);

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Equal((int)(midnight - h.Time.GetUtcNow().UtcDateTime).TotalSeconds, int.Parse(Assert.Single(refused.Headers.GetValues("Retry-After"))));
        Assert.Equal(HttpStatusCode.OK, (await h.Anonymous.GetAsync(other)).StatusCode);

        // The next UTC day: a new list (the old link has expired by then), and the allowance is whole again.
        h.Time.Advance(midnight - h.Time.GetUtcNow().UtcDateTime + TimeSpan.FromMinutes(1));
        Assert.Equal(HttpStatusCode.OK, (await h.Anonymous.GetAsync(await TileUrlAsync(h))).StatusCode);
    }
}
