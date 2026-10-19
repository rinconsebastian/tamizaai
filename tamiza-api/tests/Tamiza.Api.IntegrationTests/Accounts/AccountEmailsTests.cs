using Microsoft.Extensions.DependencyInjection;
using Tamiza.Api.Accounts;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.Accounts;

/// <summary>The policy of account emails, with a fake identity provider and a real database.</summary>
[Collection(PostgresCollection.Name)]
public sealed class AccountEmailsTests(PostgresFixture postgres) : IAsyncLifetime
{
    private TamizaApiFactory _factory = null!;
    private string _db = "";
    private Guid _ada;
    private readonly FakeAccountProvider _provider = new();
    private readonly AdjustableTimeProvider _time = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private async Task SetUpAsync(int perHour = 20)
    {
        _db = await postgres.CreateDatabaseAsync();
        _factory = new TamizaApiFactory(_db, new Dictionary<string, string?> { ["TAMIZA_ACCOUNT_EMAILS_PER_HOUR"] = perHour.ToString() }, services =>
        {
            services.AddSingleton<IAccountProviderSource>(_provider);
            services.AddSingleton<TimeProvider>(_time);
        });
        _ada = await TestData.SignInAsync(_factory, "ada", "ada@example.org");
    }

    private async Task<T> RunAsync<T>(Func<AccountEmails, Task<T>> action)
    {
        using var scope = _factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<AccountEmails>());
    }

    private Task<EmailResult?> InviteAsync(string email, Guid? requester = null, string? language = "es") =>
        RunAsync(service => service.SendInvitationAsync(email, requester ?? _ada, language, default));

    private Task AddOwnedAsync(string accountId, string email, bool lastEmail = false) =>
        TestData.ExecuteAsync(_db,
            "INSERT INTO tamiza.invited_accounts (account_id, normalized_email, created_by, created_at, last_email_at) "
            + "VALUES (@id, @email, @by, now(), CASE WHEN @last THEN now() END)",
            ("id", accountId), ("email", email), ("by", _ada), ("last", lastEmail));

    private Task<long> RowsAsync() => TestData.ScalarAsync<long>(_db, "SELECT count(*) FROM tamiza.invited_accounts");

    // Invitation

    [Fact]
    public async Task A_new_person_gets_an_account_a_record_and_the_setup_email()
    {
        await SetUpAsync();

        var result = await InviteAsync("ana@example.org");

        Assert.Equal(EmailOutcome.Sent, result!.Value.Outcome);
        Assert.Equal(["find-by-email", "create", "send-setup:new-1"], _provider.Calls);
        Assert.Equal("es", _provider.LastLanguage);
        Assert.Equal("ana@example.org", await TestData.ScalarAsync<string>(_db, "SELECT normalized_email FROM tamiza.invited_accounts WHERE account_id = 'new-1'"));
        Assert.Equal(_ada, await TestData.ScalarAsync<Guid>(_db, "SELECT created_by FROM tamiza.invited_accounts WHERE account_id = 'new-1'"));
        Assert.True(await TestData.ScalarAsync<bool>(_db, "SELECT last_email_at IS NOT NULL FROM tamiza.invited_accounts WHERE account_id = 'new-1'"));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task An_account_made_outside_tamiza_is_never_created_changed_or_emailed(bool verified, bool enabled)
    {
        await SetUpAsync();
        _provider.AddAccount("ana@example.org", verified: verified, enabled: enabled);

        var result = await InviteAsync("ana@example.org");

        Assert.Equal(EmailOutcome.ExistingAccount, result!.Value.Outcome);
        Assert.Equal(["find-by-email"], _provider.Calls);
        Assert.Equal(0, await RowsAsync());
    }

    [Fact]
    public async Task An_invitation_not_used_yet_is_sent_again_without_a_second_account()
    {
        await SetUpAsync();
        var account = _provider.AddAccount("ana@example.org", verified: false);
        await AddOwnedAsync(account.Id, "ana@example.org");

        var result = await InviteAsync("ana@example.org");

        Assert.Equal(EmailOutcome.Sent, result!.Value.Outcome);
        Assert.Equal(["find-by-email", "send-setup:" + account.Id], _provider.Calls);
        Assert.Equal(1, await RowsAsync());
        Assert.True(await TestData.ScalarAsync<bool>(_db, "SELECT last_email_at IS NOT NULL FROM tamiza.invited_accounts"));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task A_created_account_that_is_verified_or_disabled_is_left_alone(bool verified, bool enabled)
    {
        await SetUpAsync();
        var account = _provider.AddAccount("ana@example.org", verified: verified, enabled: enabled);
        await AddOwnedAsync(account.Id, "ana@example.org");

        var result = await InviteAsync("ana@example.org");

        Assert.Equal(EmailOutcome.ExistingAccount, result!.Value.Outcome);
        Assert.Equal(["find-by-email"], _provider.Calls);
    }

    [Fact]
    public async Task The_same_person_is_not_emailed_again_within_ten_minutes()
    {
        await SetUpAsync();
        Assert.Equal(EmailOutcome.Sent, (await InviteAsync("ana@example.org"))!.Value.Outcome);

        _time.Advance(TimeSpan.FromMinutes(5));
        var soon = await InviteAsync("ana@example.org");
        Assert.Equal(EmailOutcome.RecentlySent, soon!.Value.Outcome);
        Assert.InRange(soon.Value.RetryAfterSeconds, 290, 301);
        Assert.Equal(1, _provider.Calls.Count(c => c.StartsWith("send-setup", StringComparison.Ordinal)));

        _time.Advance(TimeSpan.FromMinutes(6));
        Assert.Equal(EmailOutcome.Sent, (await InviteAsync("ana@example.org"))!.Value.Outcome);
        Assert.Equal(1, _provider.Calls.Count(c => c == "create"));
    }

    [Fact]
    public async Task The_hourly_limit_stops_a_user_before_any_provider_call_and_returns_after_the_hour()
    {
        await SetUpAsync(perHour: 2);
        await InviteAsync("a@example.org");
        await InviteAsync("b@example.org");
        var calls = _provider.Calls.Count;

        var refused = await InviteAsync("c@example.org");

        Assert.Equal(EmailOutcome.RateLimited, refused!.Value.Outcome);
        Assert.InRange(refused.Value.RetryAfterSeconds, 3500, 3600);
        Assert.Equal(calls, _provider.Calls.Count);
        Assert.Equal(2, await RowsAsync());

        _time.Advance(TimeSpan.FromMinutes(61));
        Assert.Equal(EmailOutcome.Sent, (await InviteAsync("c@example.org"))!.Value.Outcome);
    }

    [Fact]
    public async Task Existing_accounts_and_recent_emails_do_not_use_the_allowance()
    {
        await SetUpAsync(perHour: 2);
        _provider.AddAccount("outside@example.org");
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(EmailOutcome.ExistingAccount, (await InviteAsync("outside@example.org"))!.Value.Outcome);
        }

        Assert.Equal(EmailOutcome.Sent, (await InviteAsync("ana@example.org"))!.Value.Outcome);
        Assert.Equal(EmailOutcome.RecentlySent, (await InviteAsync("ana@example.org"))!.Value.Outcome);
        Assert.Equal(EmailOutcome.RecentlySent, (await InviteAsync("ana@example.org"))!.Value.Outcome);

        // Only the one email actually requested used the allowance: a second still fits, a third does not.
        Assert.Equal(EmailOutcome.Sent, (await InviteAsync("bea@example.org"))!.Value.Outcome);
        Assert.Equal(EmailOutcome.RateLimited, (await InviteAsync("cris@example.org"))!.Value.Outcome);
    }

    [Fact]
    public async Task The_allowance_belongs_to_the_user_who_asked()
    {
        await SetUpAsync(perHour: 1);
        var grace = await TestData.SignInAsync(_factory, "grace", "grace@example.org");

        Assert.Equal(EmailOutcome.Sent, (await InviteAsync("a@example.org"))!.Value.Outcome);
        Assert.Equal(EmailOutcome.RateLimited, (await InviteAsync("b@example.org"))!.Value.Outcome);
        Assert.Equal(EmailOutcome.Sent, (await InviteAsync("c@example.org", grace))!.Value.Outcome);
    }

    [Fact]
    public async Task A_send_that_fails_after_the_account_was_created_keeps_the_record_and_a_later_call_sends()
    {
        await SetUpAsync();
        _provider.SendFailure = ProviderFailure.Other;

        var failed = await InviteAsync("ana@example.org");

        Assert.Equal(EmailOutcome.Failed, failed!.Value.Outcome);
        Assert.Equal(1, await RowsAsync());
        Assert.False(await TestData.ScalarAsync<bool>(_db, "SELECT last_email_at IS NOT NULL FROM tamiza.invited_accounts"));

        _provider.SendFailure = null;
        var later = await InviteAsync("ana@example.org");

        Assert.Equal(EmailOutcome.Sent, later!.Value.Outcome);
        Assert.Equal(1, _provider.Calls.Count(c => c == "create"));
        Assert.True(await TestData.ScalarAsync<bool>(_db, "SELECT last_email_at IS NOT NULL FROM tamiza.invited_accounts"));
    }

    [Theory]
    [InlineData(ProviderFailure.Unreachable)]
    [InlineData(ProviderFailure.Unauthorized)]
    [InlineData(ProviderFailure.Other)]
    public async Task A_failing_lookup_or_create_is_a_failed_outcome_and_records_nothing(ProviderFailure failure)
    {
        await SetUpAsync();
        _provider.FindFailure = failure;
        Assert.Equal(EmailOutcome.Failed, (await InviteAsync("ana@example.org"))!.Value.Outcome);

        _provider.FindFailure = null;
        _provider.CreateFailure = failure;
        Assert.Equal(EmailOutcome.Failed, (await InviteAsync("ana@example.org"))!.Value.Outcome);
        Assert.Equal(0, await RowsAsync());
    }

    [Fact]
    public async Task An_account_whose_creation_reply_was_lost_is_found_recorded_and_emailed()
    {
        await SetUpAsync();
        _provider.LoseCreateReply = true;

        var result = await InviteAsync("ana@example.org");

        Assert.Equal(EmailOutcome.Sent, result!.Value.Outcome);
        Assert.Equal(["find-by-email", "create", "find-by-email", "send-setup:new-1"], _provider.Calls);
        Assert.Equal(1, await RowsAsync());
        Assert.Equal("new-1", await TestData.ScalarAsync<string>(_db, "SELECT account_id FROM tamiza.invited_accounts"));

        // Without the record, a later invitation would have refused the account as one Tamiza did not make.
        _provider.LoseCreateReply = false;
        _time.Advance(TimeSpan.FromMinutes(11));
        Assert.Equal(EmailOutcome.Sent, (await InviteAsync("ana@example.org"))!.Value.Outcome);
    }

    [Fact]
    public async Task A_lost_creation_reply_for_an_account_that_was_not_made_is_a_failed_outcome()
    {
        await SetUpAsync();
        _provider.CreateFailure = ProviderFailure.Unreachable;

        var result = await InviteAsync("ana@example.org");

        Assert.Equal(EmailOutcome.Failed, result!.Value.Outcome);
        Assert.Equal(["find-by-email", "create", "find-by-email"], _provider.Calls);
        Assert.Equal(0, await RowsAsync());
    }

    [Fact]
    public async Task A_conflict_on_create_means_the_account_exists_now()
    {
        await SetUpAsync();
        _provider.CreateFailure = ProviderFailure.Conflict;

        var result = await InviteAsync("ana@example.org");

        Assert.Equal(EmailOutcome.ExistingAccount, result!.Value.Outcome);
        Assert.Equal(0, await RowsAsync());
    }

    [Fact]
    public async Task Two_requests_for_the_same_new_address_create_one_account()
    {
        await SetUpAsync();

        var results = await Task.WhenAll(
            InviteAsync("ana@example.org"), InviteAsync("ana@example.org"), InviteAsync("ana@example.org"));

        Assert.Equal(1, _provider.Calls.Count(c => c == "create"));
        Assert.Equal(1, results.Count(r => r!.Value.Outcome == EmailOutcome.Sent));
        Assert.All(results.Where(r => r!.Value.Outcome != EmailOutcome.Sent), r => Assert.Equal(EmailOutcome.RecentlySent, r!.Value.Outcome));
        Assert.Equal(1, await RowsAsync());
    }

    [Fact]
    public async Task Without_a_provider_nothing_is_done_and_nothing_is_reported()
    {
        await SetUpAsync();
        await _factory.DisposeAsync();
        var plain = new TamizaApiFactory(_db);
        try
        {
            using var scope = plain.Services.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<AccountEmails>();

            Assert.False(service.Enabled);
            Assert.Null(await service.SendInvitationAsync("ana@example.org", _ada, "es", default));
            Assert.Null(await service.SendPasswordResetAsync("sub-1", _ada, default));
        }
        finally
        {
            await plain.DisposeAsync();
            _factory = new TamizaApiFactory(_db);
        }
    }

    // Password reset

    [Fact]
    public async Task A_member_gets_the_reset_email_at_the_account_found_by_their_subject()
    {
        await SetUpAsync();
        var account = _provider.AddAccount("grace@example.org", verified: true);

        var result = await RunAsync(service => service.SendPasswordResetAsync(account.Id, _ada, default));

        Assert.Equal(EmailOutcome.Sent, result!.Value.Outcome);
        Assert.Equal(["find-by-subject", "send-reset:" + account.Id], _provider.Calls);
        Assert.Equal(0, await RowsAsync());
    }

    [Fact]
    public async Task A_missing_disabled_or_emailless_account_has_nobody_to_email()
    {
        await SetUpAsync();
        var disabled = _provider.AddAccount("d@example.org", enabled: false);
        var noEmail = _provider.AddAccount("n@example.org", hasEmail: false);

        foreach (var subject in new[] { disabled.Id, noEmail.Id, "not-an-account" })
        {
            var result = await RunAsync(service => service.SendPasswordResetAsync(subject, _ada, default));
            Assert.Equal(EmailOutcome.NoAccount, result!.Value.Outcome);
        }

        Assert.DoesNotContain(_provider.Calls, call => call.StartsWith("send-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_account_the_provider_refuses_to_email_is_reported_as_no_account()
    {
        await SetUpAsync();
        var account = _provider.AddAccount("grace@example.org");
        _provider.SendFailure = ProviderFailure.AccountUnavailable;

        var result = await RunAsync(service => service.SendPasswordResetAsync(account.Id, _ada, default));

        Assert.Equal(EmailOutcome.NoAccount, result!.Value.Outcome);
    }

    [Fact]
    public async Task Reset_and_invitation_emails_share_the_ten_minute_interval_and_the_hourly_allowance()
    {
        await SetUpAsync(perHour: 3);
        var account = _provider.AddAccount("ana@example.org", verified: false);
        await AddOwnedAsync(account.Id, "ana@example.org");

        Assert.Equal(EmailOutcome.Sent, (await InviteAsync("ana@example.org"))!.Value.Outcome);
        var soon = await RunAsync(service => service.SendPasswordResetAsync(account.Id, _ada, default));
        Assert.Equal(EmailOutcome.RecentlySent, soon!.Value.Outcome);

        _time.Advance(TimeSpan.FromMinutes(11));
        Assert.Equal(EmailOutcome.Sent, (await RunAsync(service => service.SendPasswordResetAsync(account.Id, _ada, default)))!.Value.Outcome);
        _time.Advance(TimeSpan.FromMinutes(11));
        Assert.Equal(EmailOutcome.Sent, (await InviteAsync("ana@example.org"))!.Value.Outcome);
        _time.Advance(TimeSpan.FromMinutes(11));

        var limited = await RunAsync(service => service.SendPasswordResetAsync(account.Id, _ada, default));

        Assert.Equal(EmailOutcome.RateLimited, limited!.Value.Outcome);
    }

    [Fact]
    public async Task A_failing_provider_during_a_reset_is_a_failed_outcome()
    {
        await SetUpAsync();
        var account = _provider.AddAccount("grace@example.org");

        _provider.FindFailure = ProviderFailure.Unreachable;
        Assert.Equal(EmailOutcome.Failed, (await RunAsync(service => service.SendPasswordResetAsync(account.Id, _ada, default)))!.Value.Outcome);

        _provider.FindFailure = null;
        _provider.SendFailure = ProviderFailure.Other;
        Assert.Equal(EmailOutcome.Failed, (await RunAsync(service => service.SendPasswordResetAsync(account.Id, _ada, default)))!.Value.Outcome);

        // A failed send does not start the ten-minute interval.
        _provider.SendFailure = null;
        Assert.Equal(EmailOutcome.Sent, (await RunAsync(service => service.SendPasswordResetAsync(account.Id, _ada, default)))!.Value.Outcome);
    }
}
