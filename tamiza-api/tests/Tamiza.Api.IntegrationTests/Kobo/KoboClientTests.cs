using Microsoft.Extensions.DependencyInjection;
using Tamiza.Api.IntegrationTests.Infrastructure;
using Tamiza.Api.Kobo;

namespace Tamiza.Api.IntegrationTests.Kobo;

[Collection(PostgresCollection.Name)]
public sealed class KoboClientTests(PostgresFixture postgres) : IDisposable
{
    private readonly KoboStub _kobo = new();

    private static readonly Dictionary<string, string?> AllowPrivate = new() { ["TAMIZA_KOBO_ALLOW_PRIVATE_NETWORKS"] = "true" };

    private async Task<TamizaApiFactory> FactoryAsync(Dictionary<string, string?>? settings = null) =>
        new(await postgres.CreateDatabaseAsync(), settings ?? AllowPrivate,
            services => services.Configure<KoboHttpOptions>(o => o.Timeout = TimeSpan.FromSeconds(1)));

    private static KoboClient Client(TamizaApiFactory factory)
    {
        factory.CreateClient().Dispose(); // start the host
        return factory.Services.GetRequiredService<KoboClient>();
    }

    [Fact]
    public async Task Verifies_access_and_reads_the_form_sending_the_token_header()
    {
        _kobo.WithForm();
        await using var factory = await FactoryAsync();

        var (asset, error) = await Client(factory).VerifyAccessAsync(_kobo.Url, KoboStub.AssetUid, KoboStub.Token, default);

        Assert.Null(error);
        Assert.Equal("Household survey", asset!.Name);
        Assert.Equal(5, asset.Fields.Count);
        Assert.Equal(2, _kobo.Server.LogEntries.Count());
        Assert.All(_kobo.Server.LogEntries, entry => Assert.Equal($"Token {KoboStub.Token}", entry.RequestMessage!.Headers!["Authorization"].Single()));
        AssertTokenNotLogged(factory);
    }

    [Theory]
    [InlineData(401, KoboErrorCodes.Unauthorized)]
    [InlineData(403, KoboErrorCodes.Unauthorized)]
    [InlineData(404, KoboErrorCodes.FormNotFound)]
    [InlineData(302, KoboErrorCodes.UnexpectedResponse)]
    [InlineData(500, KoboErrorCodes.UnexpectedResponse)]
    public async Task Maps_error_responses_to_codes(int status, string code)
    {
        _kobo.Respond(_kobo.AssetPath, status);
        await using var factory = await FactoryAsync();

        var (_, error) = await Client(factory).VerifyAccessAsync(_kobo.Url, KoboStub.AssetUid, KoboStub.Token, default);

        Assert.Equal(code, error?.Code);
        AssertTokenNotLogged(factory);
    }

    [Fact]
    public async Task Form_readable_but_submissions_forbidden_is_unauthorized()
    {
        _kobo.WithForm(submissionsStatus: 403);
        await using var factory = await FactoryAsync();

        var (_, error) = await Client(factory).VerifyAccessAsync(_kobo.Url, KoboStub.AssetUid, KoboStub.Token, default);

        Assert.Equal(KoboErrorCodes.Unauthorized, error?.Code);
        Assert.Contains("submissions", error!.Message);
    }

    [Fact]
    public async Task Timeout_is_unreachable()
    {
        _kobo.Respond(_kobo.AssetPath, 200, delay: TimeSpan.FromSeconds(3));
        await using var factory = await FactoryAsync();

        var (_, error) = await Client(factory).VerifyAccessAsync(_kobo.Url, KoboStub.AssetUid, KoboStub.Token, default);

        Assert.Equal(KoboErrorCodes.Unreachable, error?.Code);
    }

    private static SubmissionRead Read(DateTimeOffset? since = null, bool idsOnly = false, int pageSize = 3, long maxBytes = 1024 * 1024, TimeSpan? timeout = null) =>
        new(since, idsOnly, pageSize, maxBytes, timeout ?? TimeSpan.FromSeconds(10));

    private static async Task<List<SubmissionPage>> ReadAllAsync(KoboClient client, string url, SubmissionRead read)
    {
        var pages = new List<SubmissionPage>();
        await foreach (var page in client.ReadSubmissionsAsync(url, KoboStub.AssetUid, KoboStub.Token, read, default))
        {
            pages.Add(page);
        }

        return pages;
    }

    [Fact]
    public async Task Reads_submissions_in_id_pages_until_a_short_page()
    {
        _kobo.WithForm().WithSubmissions(KoboStub.MakeSubmissions(8, firstId: 101));
        await using var factory = await FactoryAsync();

        var pages = await ReadAllAsync(Client(factory), _kobo.Url, Read());

        Assert.Equal([3, 3, 2], pages.Select(p => p.Rows.Count));
        Assert.Equal(Enumerable.Range(101, 8).Select(i => (long)i), pages.SelectMany(p => p.Ids));
        Assert.Equal([null, 103L, 106L], _kobo.DataRequests.Select(r => r.AfterId));
        Assert.All(_kobo.DataRequests, r => Assert.Equal(3, r.Limit));
        Assert.Equal("uuid-101", pages[0].Rows[0].KoboUuid);
        AssertTokenNotLogged(factory);
    }

    [Fact]
    public async Task A_full_last_page_is_followed_by_an_empty_one()
    {
        _kobo.WithForm().WithSubmissions(KoboStub.MakeSubmissions(6));
        await using var factory = await FactoryAsync();

        var pages = await ReadAllAsync(Client(factory), _kobo.Url, Read());

        Assert.Equal([3, 3, 0], pages.Select(p => p.Rows.Count));
    }

    [Fact]
    public async Task The_cutoff_filters_by_submission_time_and_ids_only_skips_the_records()
    {
        _kobo.WithForm().WithSubmissions([
            KoboStub.Submission(1, "2026-09-01T08:00:00"), KoboStub.Submission(2, "2026-09-01T10:00:00"), KoboStub.Submission(3, "2026-09-02T00:00:00")]);
        await using var factory = await FactoryAsync();
        var client = Client(factory);

        var recent = await ReadAllAsync(client, _kobo.Url, Read(since: new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero)));
        var ids = await ReadAllAsync(client, _kobo.Url, Read(idsOnly: true));

        Assert.Equal([2L, 3L], recent.SelectMany(p => p.Ids));
        Assert.Equal("2026-09-01T09:00:00", _kobo.DataRequests[0].SubmittedSince);
        Assert.Equal([1L, 2L, 3L], ids.SelectMany(p => p.Ids));
        Assert.Empty(ids.SelectMany(p => p.Rows));
        Assert.True(_kobo.DataRequests[^1].IdsOnly);
    }

    [Fact]
    public async Task A_page_over_the_byte_cap_fails_with_unexpected_response()
    {
        _kobo.WithForm().WithSubmissions(KoboStub.MakeSubmissions(3));
        await using var factory = await FactoryAsync();

        var error = await Assert.ThrowsAsync<KoboReadException>(() => ReadAllAsync(Client(factory), _kobo.Url, Read(maxBytes: 100)));

        Assert.Equal(KoboErrorCodes.UnexpectedResponse, error.Error.Code);
    }

    [Fact]
    public async Task A_slow_page_fails_as_unreachable()
    {
        _kobo.WithForm().WithSubmissions(KoboStub.MakeSubmissions(3));
        _kobo.DataDelay = TimeSpan.FromSeconds(2);
        await using var factory = await FactoryAsync();

        var error = await Assert.ThrowsAsync<KoboReadException>(() => ReadAllAsync(Client(factory), _kobo.Url, Read(timeout: TimeSpan.FromMilliseconds(300))));

        Assert.Equal(KoboErrorCodes.Unreachable, error.Error.Code);
    }

    [Fact]
    public async Task A_rejection_mid_read_fails_after_the_pages_already_returned()
    {
        _kobo.WithForm().WithSubmissions(KoboStub.MakeSubmissions(9));
        _kobo.DataFailure = request => request.Index == 1 ? 401 : null;
        await using var factory = await FactoryAsync();
        var pages = new List<SubmissionPage>();

        var error = await Assert.ThrowsAsync<KoboReadException>(async () =>
        {
            await foreach (var page in Client(factory).ReadSubmissionsAsync(_kobo.Url, KoboStub.AssetUid, KoboStub.Token, Read(), default))
            {
                pages.Add(page);
            }
        });

        Assert.Equal(KoboErrorCodes.Unauthorized, error.Error.Code);
        Assert.Single(pages);
    }

    [Fact]
    public async Task Unresolvable_host_is_unreachable()
    {
        await using var factory = await FactoryAsync();

        var (_, error) = await Client(factory).VerifyAccessAsync("https://kobo.tamiza-test.invalid", KoboStub.AssetUid, KoboStub.Token, default);

        Assert.Equal(KoboErrorCodes.Unreachable, error?.Code);
        AssertTokenNotLogged(factory);
    }

    [Fact]
    public async Task Private_address_is_refused_without_sending_a_request_unless_allowed()
    {
        _kobo.WithForm();
        var loopbackUrl = _kobo.Url.Replace("localhost", "127.0.0.1");

        await using (var strict = await FactoryAsync(new Dictionary<string, string?> { ["TAMIZA_KOBO_ALLOW_PRIVATE_NETWORKS"] = "false" }))
        {
            strict.CreateClient().Dispose();
            var http = strict.Services.GetRequiredService<IHttpClientFactory>().CreateClient(KoboHttp.ClientName);
            var exception = await Assert.ThrowsAsync<HttpRequestException>(() => http.GetAsync($"{loopbackUrl}{_kobo.AssetPath}"));
            Assert.IsType<KoboAddressNotAllowedException>(exception.InnerException);

            var (_, error) = await strict.Services.GetRequiredService<KoboClient>().VerifyAccessAsync(loopbackUrl, KoboStub.AssetUid, KoboStub.Token, default);
            Assert.Equal(KoboErrorCodes.AddressNotAllowed, error?.Code);
            Assert.Empty(_kobo.Server.LogEntries);
        }

        await using var permissive = await FactoryAsync();
        var (asset, allowedError) = await Client(permissive).VerifyAccessAsync(loopbackUrl, KoboStub.AssetUid, KoboStub.Token, default);
        Assert.Null(allowedError);
        Assert.NotNull(asset);
        Assert.NotEmpty(_kobo.Server.LogEntries);
    }

    private static void AssertTokenNotLogged(TamizaApiFactory factory) =>
        Assert.DoesNotContain(factory.Logs.Lines, line => line.Contains(KoboStub.Token));

    public void Dispose() => _kobo.Dispose();
}
