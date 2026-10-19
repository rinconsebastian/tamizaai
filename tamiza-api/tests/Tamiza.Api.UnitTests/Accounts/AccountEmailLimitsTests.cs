using Microsoft.Extensions.Options;
using Tamiza.Api.Accounts;
using Tamiza.Api.UnitTests.Maps;

namespace Tamiza.Api.UnitTests.Accounts;

public sealed class AccountEmailLimitsTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 18, 9, 0, 0, TimeSpan.Zero);

    private static (AccountEmailLimits Limits, ManualTime Time) Create(int perHour)
    {
        var time = new ManualTime(Start);
        return (new AccountEmailLimits(Options.Create(new AccountEmailOptions { PerHour = perHour }), time), time);
    }

    [Fact]
    public void A_user_gets_the_configured_number_of_emails_per_hour()
    {
        var (limits, _) = Create(3);
        var user = Guid.NewGuid();

        Assert.True(limits.TryTake(user).Allowed);
        Assert.True(limits.TryTake(user).Allowed);
        Assert.True(limits.TryTake(user).Allowed);
        var refused = limits.TryTake(user);

        Assert.False(refused.Allowed);
        Assert.Equal(3600, refused.RetryAfterSeconds);
    }

    [Fact]
    public void Users_do_not_share_their_allowance()
    {
        var (limits, _) = Create(1);

        Assert.True(limits.TryTake(Guid.NewGuid()).Allowed);
        Assert.True(limits.TryTake(Guid.NewGuid()).Allowed);
    }

    [Fact]
    public void Peeking_uses_nothing_and_sees_a_spent_allowance()
    {
        var (limits, _) = Create(1);
        var user = Guid.NewGuid();

        Assert.True(limits.Peek(user).Allowed);
        Assert.True(limits.Peek(user).Allowed);
        Assert.True(limits.TryTake(user).Allowed);
        Assert.False(limits.Peek(user).Allowed);
    }

    [Fact]
    public void The_wait_shrinks_as_the_hour_passes_and_the_allowance_returns_after_it()
    {
        var (limits, time) = Create(1);
        var user = Guid.NewGuid();
        limits.TryTake(user);

        time.Advance(TimeSpan.FromMinutes(45));
        Assert.Equal(15 * 60, limits.TryTake(user).RetryAfterSeconds);
        time.Advance(TimeSpan.FromMinutes(15));

        Assert.True(limits.TryTake(user).Allowed);
    }

    [Fact]
    public void An_account_may_be_sent_another_email_after_ten_minutes()
    {
        var (limits, time) = Create(20);

        Assert.Equal(0, limits.IntervalRemaining("acc-1"));
        limits.MarkSent("acc-1");
        Assert.Equal(600, limits.IntervalRemaining("acc-1"));
        time.Advance(TimeSpan.FromMinutes(4));
        Assert.Equal(360, limits.IntervalRemaining("acc-1"));
        Assert.Equal(0, limits.IntervalRemaining("acc-2"));
        time.Advance(TimeSpan.FromMinutes(6));

        Assert.Equal(0, limits.IntervalRemaining("acc-1"));
    }

    [Fact]
    public async Task One_key_is_handled_by_one_request_at_a_time()
    {
        var (limits, _) = Create(20);
        var first = await limits.LockAsync("ana@example.org", default);

        var second = limits.LockAsync("ana@example.org", default);
        await Task.Delay(50);
        Assert.False(second.IsCompleted);

        first.Dispose();
        first.Dispose();
        using var acquired = await second.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
