using Microsoft.Extensions.Options;
using Tamiza.Api.Mcp;

namespace Tamiza.Api.UnitTests.Mcp;

public sealed class McpLimiterTests
{
    private static (McpLimiter Limiter, ManualTime Time) Create(int perMinute = 120, int concurrent = 4)
    {
        var time = new ManualTime(new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero));
        var options = Options.Create(new McpOptions { CallsPerMinutePerUser = perMinute, MaxConcurrentCallsPerUser = concurrent });
        return (new McpLimiter(options, time), time);
    }

    [Fact]
    public void Calls_over_the_minute_limit_are_refused_with_the_seconds_to_wait()
    {
        var (limiter, time) = Create(perMinute: 10);
        var user = Guid.NewGuid();
        for (var i = 0; i < 10; i++)
        {
            limiter.TryEnter(user, out _)!.Dispose();
            time.Advance(TimeSpan.FromSeconds(1));
        }

        var refused = limiter.TryEnter(user, out var retryAfter);

        Assert.Null(refused);
        // The first call was 10 seconds ago, so it leaves the window in 50.
        Assert.Equal(50, retryAfter);
    }

    [Fact]
    public void The_window_slides_so_old_calls_stop_counting()
    {
        var (limiter, time) = Create(perMinute: 10);
        var user = Guid.NewGuid();
        for (var i = 0; i < 10; i++)
        {
            limiter.TryEnter(user, out _)!.Dispose();
        }

        Assert.Null(limiter.TryEnter(user, out _));
        time.Advance(TimeSpan.FromSeconds(61));

        Assert.NotNull(limiter.TryEnter(user, out _));
    }

    [Fact]
    public void Calls_over_the_concurrency_limit_are_refused_until_one_ends()
    {
        var (limiter, _) = Create(concurrent: 2);
        var user = Guid.NewGuid();
        var first = limiter.TryEnter(user, out _);
        var second = limiter.TryEnter(user, out _);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Null(limiter.TryEnter(user, out var retryAfter));
        Assert.Equal(2, retryAfter);

        first!.Dispose();

        Assert.NotNull(limiter.TryEnter(user, out _));
    }

    [Fact]
    public void Disposing_a_place_twice_frees_it_once()
    {
        var (limiter, _) = Create(concurrent: 1);
        var user = Guid.NewGuid();
        var slot = limiter.TryEnter(user, out _)!;
        slot.Dispose();
        slot.Dispose();
        var next = limiter.TryEnter(user, out _);

        Assert.NotNull(next);
        Assert.Null(limiter.TryEnter(user, out _));
    }

    [Fact]
    public void A_refused_call_is_not_counted_and_people_do_not_share_limits()
    {
        var (limiter, _) = Create(perMinute: 10, concurrent: 1);
        var ada = Guid.NewGuid();
        var grace = Guid.NewGuid();
        var held = limiter.TryEnter(ada, out _);
        for (var i = 0; i < 50; i++)
        {
            Assert.Null(limiter.TryEnter(ada, out _));
        }

        held!.Dispose();

        Assert.NotNull(limiter.TryEnter(ada, out _));
        Assert.NotNull(limiter.TryEnter(grace, out _));
    }

    /// <summary>A clock that only moves when a test moves it.</summary>
    private sealed class ManualTime(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
