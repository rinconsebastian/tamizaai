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
