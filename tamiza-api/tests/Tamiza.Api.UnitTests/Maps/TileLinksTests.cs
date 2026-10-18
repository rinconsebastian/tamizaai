using Microsoft.AspNetCore.DataProtection;
using Tamiza.Api.Maps;

namespace Tamiza.Api.UnitTests.Maps;

public sealed class TileLinksTests
{
    private static readonly Guid Ada = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Grace = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static ScriptedHandler NoGoogle() => new((_, _) => throw new InvalidOperationException("Google must not be called."));

    private static DateTimeOffset WindowEnd(MapsServices services)
    {
        var seconds = 45 * 60;
        var now = services.Time.GetUtcNow().ToUnixTimeSeconds();
        return DateTimeOffset.FromUnixTimeSeconds((now / seconds + 1) * seconds);
    }

    [Fact]
    public void A_link_round_trips_to_its_user_and_basemap()
    {
        using var services = new MapsServices(NoGoogle());

        var link = services.Links.Issue(Ada, "satellite");
        var claims = services.Links.Read(link.Link);

        Assert.Equal(new TileLinkClaims(Ada, "satellite"), claims);
    }

    [Fact]
    public void A_link_holds_only_url_safe_characters_and_nothing_readable()
    {
        using var services = new MapsServices(NoGoogle());

        var link = services.Links.Issue(Ada, "streets").Link;

        Assert.Matches("^[A-Za-z0-9_-]+$", link);
        Assert.DoesNotContain(Ada.ToString("N"), link);
        Assert.DoesNotContain("streets", link);
        Assert.DoesNotContain(MapsServices.Key, link);
    }

    [Fact]
    public void The_same_string_is_given_within_a_window_and_another_after_it()
    {
        using var services = new MapsServices(NoGoogle());

        var first = services.Links.Issue(Ada, "streets");
        services.Time.Advance(TimeSpan.FromMinutes(30)); // 35 minutes into the window
        var second = services.Links.Issue(Ada, "streets");
        services.Time.Advance(TimeSpan.FromMinutes(11)); // the next window
        var next = services.Links.Issue(Ada, "streets");

        Assert.Equal(first.Link, second.Link);
        Assert.Equal(first.ExpiresAt, second.ExpiresAt);
        Assert.NotEqual(first.Link, next.Link);
        Assert.Equal(first.WindowEnd + TimeSpan.FromMinutes(45), next.WindowEnd);
    }

    [Fact]
    public void Each_user_and_each_basemap_has_its_own_link()
    {
        using var services = new MapsServices(NoGoogle());

        var links = new[]
        {
            services.Links.Issue(Ada, "streets").Link, services.Links.Issue(Ada, "satellite").Link,
            services.Links.Issue(Grace, "streets").Link, services.Links.Issue(Grace, "satellite").Link,
        };

        Assert.Equal(4, links.Distinct().Count());
        Assert.Equal(new TileLinkClaims(Grace, "satellite"), services.Links.Read(links[3]));
    }

    [Fact]
    public void The_link_expires_a_quarter_of_an_hour_after_its_window()
    {
        using var services = new MapsServices(NoGoogle());

        var link = services.Links.Issue(Ada, "streets");

        Assert.Equal(WindowEnd(services), link.WindowEnd);
        Assert.Equal(link.WindowEnd + TimeSpan.FromMinutes(15), link.ExpiresAt);
    }

    [Fact]
    public void A_link_is_valid_ten_minutes_after_its_window_and_not_after_the_grace()
    {
        using var services = new MapsServices(NoGoogle());
        var link = services.Links.Issue(Ada, "streets");

        services.Time.Advance(link.WindowEnd - services.Time.GetUtcNow() + TimeSpan.FromMinutes(10));
        Assert.NotNull(services.Links.Read(link.Link));

        services.Time.Advance(TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(59));
        Assert.NotNull(services.Links.Read(link.Link));

        services.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.Null(services.Links.Read(link.Link));
    }

    [Fact]
    public void No_link_outlives_an_hour_from_the_start_of_its_window()
    {
        using var services = new MapsServices(NoGoogle());
        var link = services.Links.Issue(Ada, "streets");
        var windowStart = link.WindowEnd - TimeSpan.FromMinutes(45);

        Assert.Equal(TimeSpan.FromMinutes(60), link.ExpiresAt - windowStart);
    }

    [Fact]
    public void A_clock_that_moves_forward_61_minutes_expires_every_link_issued_before()
    {
        using var services = new MapsServices(NoGoogle());
        var link = services.Links.Issue(Ada, "streets");

        services.Time.Advance(TimeSpan.FromMinutes(61));

        Assert.Null(services.Links.Read(link.Link));
    }

    [Fact]
    public void A_tampered_link_is_refused()
    {
        using var services = new MapsServices(NoGoogle());
        var link = services.Links.Issue(Ada, "streets").Link;
        var flipped = link[..10] + (link[10] == 'A' ? 'B' : 'A') + link[11..];

        Assert.Null(services.Links.Read(flipped));
        Assert.Null(services.Links.Read(link[..^2]));
        Assert.Null(services.Links.Read(""));
        Assert.Null(services.Links.Read("not-a-link"));
        Assert.Null(services.Links.Read(new string('A', 200)));
    }

    [Fact]
    public void A_payload_protected_for_another_purpose_is_refused()
    {
        using var services = new MapsServices(NoGoogle());
        var other = services.Protection.CreateProtector("Tamiza.Maps.TileLink.v2");
        var forged = other.Protect($"{Ada:N}|streets|{services.Time.GetUtcNow().ToUnixTimeSeconds()}");

        Assert.Null(services.Links.Read(forged));
    }

    [Theory]
    [InlineData("hybrid")]
    [InlineData("")]
    [InlineData("STREETS")]
    public void A_link_for_a_basemap_this_deployment_does_not_have_is_refused(string kind)
    {
        using var services = new MapsServices(NoGoogle());
        var protector = services.Protection.CreateProtector(TileLinks.Purpose);
        var link = protector.Protect($"{Ada:N}|{kind}|{services.Time.GetUtcNow().ToUnixTimeSeconds()}");

        Assert.Null(services.Links.Read(link));
    }

    [Theory]
    [InlineData("not-a-guid|streets|100")]
    [InlineData("11111111111111111111111111111111|streets")]
    [InlineData("11111111111111111111111111111111|streets|abc")]
    [InlineData("11111111111111111111111111111111|streets|-5")]
    [InlineData("11111111111111111111111111111111|streets|100|extra")]
    public void A_payload_of_the_wrong_shape_is_refused_even_when_it_was_protected_properly(string payload)
    {
        using var services = new MapsServices(NoGoogle());
        var link = services.Protection.CreateProtector(TileLinks.Purpose).Protect(payload);

        Assert.Null(services.Links.Read(link));
    }

    [Fact]
    public void The_claims_name_the_basemap_the_link_was_issued_for_so_it_cannot_be_used_for_another()
    {
        using var services = new MapsServices(NoGoogle());
        var streets = services.Links.Issue(Ada, "streets").Link;

        Assert.Equal("streets", services.Links.Read(streets)!.Kind);
        Assert.NotEqual("satellite", services.Links.Read(streets)!.Kind);
    }
}
