using System.Net;
using System.Text.Json;
using Tamiza.Api.Maps;

namespace Tamiza.Api.UnitTests.Maps;

public sealed class GoogleTileSessionsTests
{
    private static long In(TimeSpan span) => (MapsServices.DefaultStart + span).ToUnixTimeSeconds();

    /// <summary>A Google that hands out session-1, session-2... each valid for <paramref name="lasts"/>.</summary>
    private static ScriptedHandler Google(TimeSpan lasts, Func<HttpRequestMessage, HttpResponseMessage?>? other = null)
    {
        var count = 0;
        ScriptedHandler? self = null;
        self = new ScriptedHandler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath == "/v1/createSession")
            {
                return ScriptedHandler.Session($"session-{Interlocked.Increment(ref count)}", In(lasts));
            }

            return other?.Invoke(request) ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        return self;
    }

    [Fact]
    public async Task The_request_has_the_key_in_the_query_and_the_map_type_language_and_region_in_the_body()
    {
        var google = Google(TimeSpan.FromDays(14));
        using var services = new MapsServices(google, configure: o => { o.GoogleLanguage = "es-419"; o.GoogleRegion = "co"; });

        var session = await services.Sessions.GetAsync("satellite", CancellationToken.None);

        Assert.Equal("session-1", session.Token);
        var request = Assert.Single(google.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://tile.googleapis.com/v1/createSession", request.Uri.GetLeftPart(UriPartial.Path));
        Assert.Equal($"?key={MapsServices.Key}", request.Uri.Query);
        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal(["language", "mapType", "region"], body.RootElement.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(("satellite", "es-419", "CO"), (body.RootElement.GetProperty("mapType").GetString(), body.RootElement.GetProperty("language").GetString(), body.RootElement.GetProperty("region").GetString()));
    }

    [Fact]
    public async Task Streets_is_a_roadmap_session_and_the_defaults_fill_the_language_and_region()
    {
        var google = Google(TimeSpan.FromDays(14));
        using var services = new MapsServices(google);

        await services.Sessions.GetAsync("streets", CancellationToken.None);

        using var body = JsonDocument.Parse(google.Requests[0].Body!);
        Assert.Equal(("roadmap", "en-US", "US"), (body.RootElement.GetProperty("mapType").GetString(), body.RootElement.GetProperty("language").GetString(), body.RootElement.GetProperty("region").GetString()));
    }

    [Fact]
    public async Task A_session_is_reused_until_it_is_about_to_expire_and_each_kind_has_its_own()
    {
        var google = Google(TimeSpan.FromDays(14));
        using var services = new MapsServices(google);

        var first = await services.Sessions.GetAsync("streets", CancellationToken.None);
        var again = await services.Sessions.GetAsync("streets", CancellationToken.None);
        var satellite = await services.Sessions.GetAsync("satellite", CancellationToken.None);

        Assert.Equal(first.Token, again.Token);
        Assert.NotEqual(first.Token, satellite.Token);
        Assert.Equal(2, google.Requests.Count);
    }

    [Fact]
    public async Task A_session_is_renewed_once_it_is_under_an_hour_from_its_expiry()
    {
        var google = Google(TimeSpan.FromHours(3));
        using var services = new MapsServices(google);

        var first = await services.Sessions.GetAsync("streets", CancellationToken.None);
        services.Time.Advance(TimeSpan.FromMinutes(119)); // 61 minutes left
        var stillFirst = await services.Sessions.GetAsync("streets", CancellationToken.None);
        services.Time.Advance(TimeSpan.FromMinutes(2)); // 59 minutes left
        var renewed = await services.Sessions.GetAsync("streets", CancellationToken.None);

        Assert.Equal("session-1", first.Token);
        Assert.Equal("session-1", stillFirst.Token);
        Assert.Equal("session-2", renewed.Token);
    }

    [Fact]
    public async Task A_session_google_rejected_is_replaced_once_however_many_callers_noticed()
    {
        var google = Google(TimeSpan.FromDays(14));
        using var services = new MapsServices(google);
        var first = await services.Sessions.GetAsync("streets", CancellationToken.None);

        var renewals = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => services.Sessions.RenewAsync("streets", first.Token!, CancellationToken.None)));

        Assert.All(renewals, r => Assert.Equal("session-2", r.Token));
        Assert.Equal(2, google.Requests.Count);
    }

    [Fact]
    public async Task Concurrent_first_callers_create_one_session()
    {
        var release = new TaskCompletionSource();
        var count = 0;
        var google = new ScriptedHandler((request, _) =>
        {
            release.Task.Wait();
            return ScriptedHandler.Session($"session-{Interlocked.Increment(ref count)}", In(TimeSpan.FromDays(14)));
        });
        using var services = new MapsServices(google);

        var callers = Enumerable.Range(0, 10).Select(_ => Task.Run(() => services.Sessions.GetAsync("streets", CancellationToken.None))).ToArray();
        await Task.Delay(100);
        release.SetResult();
        var sessions = await Task.WhenAll(callers);

        Assert.All(sessions, s => Assert.Equal("session-1", s.Token));
        Assert.Single(google.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, GoogleOutcome.Unavailable)]
    [InlineData(HttpStatusCode.Forbidden, GoogleOutcome.Unavailable)]
    [InlineData(HttpStatusCode.InternalServerError, GoogleOutcome.Upstream)]
    [InlineData(HttpStatusCode.BadRequest, GoogleOutcome.Upstream)]
    public async Task A_refused_session_is_unavailable_for_quota_and_billing_and_upstream_otherwise(HttpStatusCode status, GoogleOutcome expected)
    {
        var google = new ScriptedHandler((_, _) => ScriptedHandler.Error(status, "forbidden", "secret google words"));
        using var services = new MapsServices(google);

        var session = await services.Sessions.GetAsync("streets", CancellationToken.None);

        Assert.Null(session.Token);
        Assert.Equal(expected, session.Failure);
        Assert.DoesNotContain(services.Logs.Lines, l => l.Contains("secret google words"));
        Assert.DoesNotContain(services.Logs.Lines, l => l.Contains(MapsServices.Key));
    }

    [Fact]
    public async Task An_answer_without_a_session_is_upstream_and_the_next_call_tries_again()
    {
        var calls = 0;
        var google = new ScriptedHandler((_, _) => ++calls == 1 ? ScriptedHandler.Json("""{"nothing":true}""") : ScriptedHandler.Session("session-ok", In(TimeSpan.FromDays(14))));
        using var services = new MapsServices(google);

        var failed = await services.Sessions.GetAsync("streets", CancellationToken.None);
        var ok = await services.Sessions.GetAsync("streets", CancellationToken.None);

        Assert.Equal(GoogleOutcome.Upstream, failed.Failure);
        Assert.Equal("session-ok", ok.Token);
    }

    [Fact]
    public async Task An_unreachable_google_is_upstream()
    {
        var google = new ScriptedHandler((_, _) => throw new HttpRequestException("connection refused"));
        using var services = new MapsServices(google);

        var session = await services.Sessions.GetAsync("streets", CancellationToken.None);

        Assert.Equal(GoogleOutcome.Upstream, session.Failure);
    }
}
