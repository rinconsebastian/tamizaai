using Tamiza.Api.Maps;

namespace Tamiza.Api.UnitTests.Maps;

public sealed class TileLimitsTests
{
    private static readonly Guid Ada = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Grace = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static MapsServices With(int perMinute = 600, int perDay = 3000, int credits = 120) =>
        new(new ScriptedHandler((_, _) => throw new InvalidOperationException()), configure: o =>
        {
            o.TilesPerMinute = perMinute;
            o.TilesPerUserPerDay = perDay;
            o.CreditsPerMinute = credits;
        });

    [Fact]
    public void The_601st_tile_in_a_minute_is_refused_with_a_wait_and_another_user_is_served()
    {
        using var services = With(perMinute: 600);

        for (var i = 0; i < 600; i++)
        {
            Assert.True(services.Limits.TryTile(Ada).Allowed);
        }

        var refused = services.Limits.TryTile(Ada);
        Assert.False(refused.Allowed);
        Assert.InRange(refused.RetryAfterSeconds, 1, 60);
        Assert.True(services.Limits.TryTile(Grace).Allowed);
    }

    [Fact]
    public void A_user_who_used_the_daily_allowance_is_refused_until_midnight_utc_and_others_are_served()
    {
        using var services = With(perMinute: 10_000, perDay: 100);
        for (var i = 0; i < 100; i++)
        {
            Assert.True(services.Limits.TryTile(Ada).Allowed);
        }

        var refused = services.Limits.TryTile(Ada);

        Assert.False(refused.Allowed);
        var midnight = new DateTimeOffset(services.Time.GetUtcNow().UtcDateTime.Date.AddDays(1), TimeSpan.Zero);
        Assert.Equal((int)(midnight - services.Time.GetUtcNow()).TotalSeconds, refused.RetryAfterSeconds);
        Assert.True(services.Limits.TryTile(Grace).Allowed);

        services.Time.Advance(midnight - services.Time.GetUtcNow());
        Assert.True(services.Limits.TryTile(Ada).Allowed);
    }

    [Fact]
    public void A_refused_minute_spends_no_day()
    {
        using var services = With(perMinute: 60, perDay: 100);
        for (var i = 0; i < 60; i++)
        {
            Assert.True(services.Limits.TryTile(Ada).Allowed);
        }

        for (var i = 0; i < 200; i++)
        {
            Assert.False(services.Limits.TryTile(Ada).Allowed);
        }

        // 60 of the 100 were spent: 40 more fit once the minute is open again, which only a new window would do.
        Assert.False(services.Limits.TryTile(Ada).Allowed);
    }

    [Fact]
    public void Credits_have_their_own_allowance_that_tiles_never_touch()
    {
        using var services = With(perMinute: 60, credits: 3);
        for (var i = 0; i < 60; i++)
        {
            services.Limits.TryTile(Ada);
        }

        Assert.False(services.Limits.TryTile(Ada).Allowed);
        Assert.True(services.Limits.TryCredits(Ada).Allowed);
        Assert.True(services.Limits.TryCredits(Ada).Allowed);
        Assert.True(services.Limits.TryCredits(Ada).Allowed);
        Assert.False(services.Limits.TryCredits(Ada).Allowed);
        Assert.True(services.Limits.TryCredits(Grace).Allowed);
    }
}
