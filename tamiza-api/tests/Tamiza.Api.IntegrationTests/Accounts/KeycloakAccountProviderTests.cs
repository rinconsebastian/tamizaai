using Microsoft.Extensions.DependencyInjection;
using Tamiza.Api.Accounts;
using Tamiza.Api.Accounts.Keycloak;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.Accounts;

[Collection(PostgresCollection.Name)]
public sealed class KeycloakAccountProviderTests(PostgresFixture postgres) : IDisposable
{
    private readonly KeycloakStub _kc = new();
    private readonly List<IDisposable> _disposables = [];

    private async Task<(TamizaApiFactory Factory, KeycloakAccountProvider Provider)> CreateAsync(
        Dictionary<string, string?>? settings = null, Action<IServiceCollection>? configure = null)
    {
        var factory = new TamizaApiFactory(await postgres.CreateDatabaseAsync(), settings ?? _kc.Settings(), configure);
        _disposables.Add(factory);
        return (factory, factory.Services.GetRequiredService<KeycloakAccountProvider>());
    }

    [Fact]
    public async Task Two_calls_fetch_one_token()
    {
        var (_, provider) = await CreateAsync();

        await provider.FindByEmailAsync("ana@example.org", default);
        await provider.FindByEmailAsync("bea@example.org", default);

        Assert.Single(_kc.TokenCalls);
        var form = System.Web.HttpUtility.ParseQueryString(_kc.TokenCalls[0].Body);
        Assert.Equal("client_credentials", form["grant_type"]);
        Assert.Equal(KeycloakStub.ClientId, form["client_id"]);
    }

    [Fact]
    public async Task An_expired_token_is_replaced()
    {
        var time = new AdjustableTimeProvider();
        _kc.TokenLifetimeSeconds = 100;
        var (_, provider) = await CreateAsync(configure: services => services.AddSingleton<TimeProvider>(time));

        await provider.FindByEmailAsync("ana@example.org", default);
        time.Advance(TimeSpan.FromSeconds(60));
        await provider.FindByEmailAsync("ana@example.org", default);
        Assert.Single(_kc.TokenCalls);

        // 30 seconds before it expires the token is replaced, so no call carries one that is about to lapse.
        time.Advance(TimeSpan.FromSeconds(15));
        await provider.FindByEmailAsync("ana@example.org", default);

        Assert.Equal(2, _kc.TokenCalls.Count);
        Assert.Equal("Bearer kc-token-2", _kc.CallsTo("GET", _kc.UsersPath).Last().Authorization);
    }

    [Fact]
    public async Task A_401_fetches_a_new_token_and_retries_once()
    {
        var (_, provider) = await CreateAsync();
        await provider.FindByEmailAsync("ana@example.org", default);

        _kc.ExpireTokens();
        var found = await provider.FindByEmailAsync("ana@example.org", default);

        Assert.True(found.Succeeded);
        Assert.Equal(2, _kc.TokenCalls.Count);
        Assert.Equal(3, _kc.CallsTo("GET", _kc.UsersPath).Count);
    }

    [Fact]
    public async Task Wrong_credentials_are_reported_as_unauthorized()
    {
        var (_, provider) = await CreateAsync(_kc.Settings(secret: "not-the-secret"));

        var found = await provider.FindByEmailAsync("ana@example.org", default);

        Assert.Equal(ProviderFailure.Unauthorized, found.Failure);
        Assert.Empty(_kc.CallsTo("GET", _kc.UsersPath));
    }

    [Fact]
    public async Task Find_by_email_sends_an_exact_search_and_reads_the_account()
    {
        var existing = _kc.AddUser("ana@example.org", emailVerified: false);
        var (_, provider) = await CreateAsync();

        var found = await provider.FindByEmailAsync("ana@example.org", default);
        var none = await provider.FindByEmailAsync("zoe@example.org", default);

        var search = _kc.CallsTo("GET", _kc.UsersPath).First();
        Assert.Equal("true", search.Query["exact"]);
        Assert.Equal("ana@example.org", search.Query["email"]);
        Assert.Equal(new ProviderAccount(existing.Id, false, true, true), found.Value);
        Assert.True(none.Succeeded);
        Assert.Null(none.Value);
    }

    [Fact]
    public async Task Find_by_subject_reads_the_account_and_maps_404_to_null()
    {
        var existing = _kc.AddUser("ana@example.org", enabled: false);
        var (_, provider) = await CreateAsync();

        var found = await provider.FindBySubjectAsync(existing.Id, default);
        var missing = await provider.FindBySubjectAsync(Guid.NewGuid().ToString(), default);

        Assert.Equal(new ProviderAccount(existing.Id, true, false, true), found.Value);
        Assert.True(missing.Succeeded);
        Assert.Null(missing.Value);
    }

    [Theory]
    [InlineData("es", true)]
    [InlineData("en", true)]
    [InlineData("fr", false)]
    [InlineData(null, false)]
    public async Task Create_sends_the_account_and_the_locale_only_for_a_supported_language(string? language, bool hasLocale)
    {
        var (_, provider) = await CreateAsync();

        var created = await provider.CreateAsync("ana@example.org", language, default);

        Assert.True(created.Succeeded);
        var user = _kc.FindUser("ana@example.org")!;
        Assert.Equal(user.Id, created.Value);
        var body = Assert.Single(_kc.CreateCalls).Json!.AsObject();
        Assert.Equal("ana@example.org", body["username"]!.GetValue<string>());
        Assert.Equal("ana@example.org", body["email"]!.GetValue<string>());
        Assert.True(body["enabled"]!.GetValue<bool>());
        Assert.False(body["emailVerified"]!.GetValue<bool>());
        Assert.False(body.ContainsKey("credentials"));
        Assert.Equal(hasLocale, body.ContainsKey("attributes"));
        if (hasLocale)
        {
            Assert.Equal(language, body["attributes"]!["locale"]![0]!.GetValue<string>());
        }
    }

    [Fact]
    public async Task Create_maps_409_to_conflict()
    {
        _kc.AddUser("ana@example.org");
        var (_, provider) = await CreateAsync();

        var created = await provider.CreateAsync("ana@example.org", "en", default);

        Assert.Equal(ProviderFailure.Conflict, created.Failure);
    }

    [Fact]
    public async Task Setup_and_reset_send_their_own_actions_to_the_same_endpoint()
    {
        var user = _kc.AddUser("ana@example.org");
        var (_, provider) = await CreateAsync();

        Assert.True((await provider.SendSetupEmailAsync(user.Id, default)).Succeeded);
        Assert.True((await provider.SendPasswordResetEmailAsync(user.Id, default)).Succeeded);

        var calls = _kc.ActionCalls;
        Assert.Equal(2, calls.Count);
        Assert.Equal($"{_kc.UsersPath}/{user.Id}/execute-actions-email", calls[0].Path);
        Assert.Equal(["UPDATE_PASSWORD", "VERIFY_EMAIL"], calls[0].Json!.AsArray().Select(a => a!.GetValue<string>()));
        Assert.Equal(["UPDATE_PASSWORD"], calls[1].Json!.AsArray().Select(a => a!.GetValue<string>()));
        Assert.All(calls, call => Assert.Equal("tamiza-ui", call.Query["client_id"]));
        Assert.All(calls, call => Assert.Equal("https://tamiza.test/", call.Query["redirect_uri"]));
    }

    [Fact]
    public async Task Without_a_lifetime_no_lifespan_is_sent_and_the_realm_setting_applies()
    {
        var user = _kc.AddUser("ana@example.org");
        var (_, provider) = await CreateAsync();

        await provider.SendSetupEmailAsync(user.Id, default);

        Assert.False(Assert.Single(_kc.ActionCalls).Query.ContainsKey("lifespan"));
    }

    [Fact]
    public async Task A_lifetime_in_hours_is_sent_as_seconds()
    {
        var user = _kc.AddUser("ana@example.org");
        var (_, provider) = await CreateAsync(_kc.Settings(linkHours: 24));

        await provider.SendSetupEmailAsync(user.Id, default);
        await provider.SendPasswordResetEmailAsync(user.Id, default);

        Assert.All(_kc.ActionCalls, call => Assert.Equal("86400", call.Query["lifespan"]));
    }

    [Fact]
    public async Task A_disabled_account_and_an_account_without_email_cannot_be_sent_anything()
    {
        var disabled = _kc.AddUser("ana@example.org", enabled: false);
        var noEmail = _kc.AddUser("unused@example.org", hasEmail: false);
        var (_, provider) = await CreateAsync();

        Assert.Equal(ProviderFailure.AccountUnavailable, (await provider.SendSetupEmailAsync(disabled.Id, default)).Failure);
        Assert.Equal(ProviderFailure.AccountUnavailable, (await provider.SendPasswordResetEmailAsync(noEmail.Id, default)).Failure);
        Assert.Equal(ProviderFailure.AccountUnavailable, (await provider.SendPasswordResetEmailAsync(Guid.NewGuid().ToString(), default)).Failure);
    }

    [Fact]
    public async Task A_rejected_redirect_is_a_failure_not_an_unavailable_account()
    {
        var user = _kc.AddUser("ana@example.org");
        _kc.ActionsFailure = (400, "Invalid parameter: redirect_uri");
        var (_, provider) = await CreateAsync();

        var sent = await provider.SendSetupEmailAsync(user.Id, default);

        Assert.Equal(ProviderFailure.Other, sent.Failure);
        Assert.Equal(400, sent.Status);
    }

    [Fact]
    public async Task Smtp_trouble_is_a_failure_with_the_status()
    {
        var user = _kc.AddUser("ana@example.org");
        _kc.ActionsFailure = (500, "Failed to send execute actions email");
        var (_, provider) = await CreateAsync();

        var sent = await provider.SendSetupEmailAsync(user.Id, default);

        Assert.Equal(ProviderFailure.Other, sent.Failure);
        Assert.Equal(500, sent.Status);
    }

    [Fact]
    public async Task An_unreachable_server_is_reported_as_unreachable()
    {
        var settings = _kc.Settings();
        _kc.Dispose();
        var (_, provider) = await CreateAsync(settings);

        var found = await provider.FindByEmailAsync("ana@example.org", default);

        Assert.Equal(ProviderFailure.Unreachable, found.Failure);
    }

    [Fact]
    public async Task A_slow_server_is_reported_as_unreachable()
    {
        _kc.AdminDelay = TimeSpan.FromSeconds(3);
        var (_, provider) = await CreateAsync(configure: services => services.Configure<KeycloakHttpOptions>(o => o.Timeout = TimeSpan.FromMilliseconds(400)));

        var found = await provider.FindByEmailAsync("ana@example.org", default);

        Assert.Equal(ProviderFailure.Unreachable, found.Failure);
    }

    [Fact]
    public async Task A_redirect_from_the_token_endpoint_is_not_followed()
    {
        using var elsewhere = new KeycloakStub();
        _kc.TokenRedirect = elsewhere.Url + elsewhere.TokenPath;
        var (_, provider) = await CreateAsync();

        var found = await provider.FindByEmailAsync("ana@example.org", default);

        Assert.False(found.Succeeded);
        Assert.Empty(elsewhere.Calls);
        Assert.Empty(_kc.CallsTo("GET", _kc.UsersPath));
    }

    [Fact]
    public async Task Nothing_the_adapter_logs_holds_the_secret_a_token_or_an_email_address()
    {
        var user = _kc.AddUser("ana@example.org");
        var (factory, provider) = await CreateAsync();
        await provider.FindByEmailAsync("ana@example.org", default);
        await provider.CreateAsync("bea@example.org", "es", default);
        _kc.ActionsFailure = (500, "Failed to send execute actions email for ana@example.org");
        await provider.SendSetupEmailAsync(user.Id, default);
        _kc.ExpireTokens();
        _kc.ActionsFailure = null;
        await provider.SendPasswordResetEmailAsync(user.Id, default);
        var wrong = await CreateAsync(_kc.Settings(secret: "wrong-secret-value"));
        await wrong.Provider.FindByEmailAsync("ana@example.org", default);

        var logs = string.Join('\n', factory.Logs.Lines.Concat(wrong.Factory.Logs.Lines));
        Assert.Contains("Keycloak", logs);
        Assert.DoesNotContain(KeycloakStub.ClientSecret, logs);
        Assert.DoesNotContain("wrong-secret-value", logs);
        Assert.DoesNotContain("kc-token-", logs);
        Assert.DoesNotContain("ana@example.org", logs);
        Assert.DoesNotContain("bea@example.org", logs);
    }

    public void Dispose()
    {
        foreach (var disposable in _disposables)
        {
            disposable.Dispose();
        }

        _kc.Dispose();
    }
}
