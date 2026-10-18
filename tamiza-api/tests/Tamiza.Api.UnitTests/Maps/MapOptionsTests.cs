using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tamiza.Api.Maps;

namespace Tamiza.Api.UnitTests.Maps;

public sealed class MapOptionsTests
{
    private const string Provider = "TAMIZA_MAP_BASEMAP_PROVIDER";
    private const string Key = "TAMIZA_MAP_GOOGLE_API_KEY";
    private const string Language = "TAMIZA_MAP_GOOGLE_LANGUAGE";
    private const string Region = "TAMIZA_MAP_GOOGLE_REGION";
    private const string PerMinute = "TAMIZA_MAP_TILES_PER_MINUTE";
    private const string PerDay = "TAMIZA_MAP_TILES_PER_USER_PER_DAY";

    private const string FakeKey = "AIzaFAKE-key_for_tests_0123456789abcdefg";

    private static MapOptions Bind(params (string Key, string Value)[] values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value))
            .Build();
        var options = new MapOptions();
        configuration.Bind(options);
        return options;
    }

    private static string[] Failures(params (string Key, string Value)[] values) =>
        new MapOptionsValidator().Validate(null, Bind(values)).Failures?.ToArray() ?? [];

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add((logLevel, formatter(state, exception)));
    }

    private static async Task<List<(LogLevel Level, string Message)>> StartupLogAsync(params (string Key, string Value)[] values)
    {
        var logger = new CapturingLogger<MapStartupCheck>();
        await new MapStartupCheck(Options.Create(Bind(values)), logger).StartAsync(CancellationToken.None);
        return logger.Lines;
    }

    // Defaults

    [Fact]
    public void Nothing_set_means_no_provider_and_the_defaults_apply()
    {
        var options = Bind();

        Assert.Equal("none", options.EffectiveProvider);
        Assert.False(options.IsGoogle);
        Assert.False(options.IsOsm);
        Assert.Equal(600, options.TilesPerMinute);
        Assert.Equal(3000, options.TilesPerUserPerDay);
        Assert.Equal("en-US", options.EffectiveGoogleLanguage);
        Assert.Equal("US", options.EffectiveGoogleRegion);
        Assert.Empty(Failures());
    }

    [Fact]
    public void The_unbound_properties_have_the_designed_values()
    {
        var options = new MapOptions();

        Assert.Equal(TimeSpan.FromMinutes(45), options.TileLinkWindow);
        Assert.Equal(TimeSpan.FromMinutes(15), options.TileLinkGrace);
        Assert.Equal(120, options.CreditsPerMinute);
    }

    [Fact]
    public void Blank_and_whitespace_strings_read_as_unset()
    {
        var options = Bind((Provider, "  "), (Key, "\n"), (Language, " "), (Region, ""));

        Assert.Equal("none", options.EffectiveProvider);
        Assert.Equal("", options.GoogleApiKey);
        Assert.Equal("", options.GoogleLanguage);
        Assert.Equal("", options.GoogleRegion);
        Assert.Empty(options.GoogleVariablesSet());
        Assert.Empty(Failures((Provider, "  "), (Key, "\n")));
    }

    [Fact]
    public void Text_is_trimmed_and_the_region_is_uppercased()
    {
        var options = Bind((Provider, " Google "), (Key, FakeKey + "\n"), (Language, " es-419 "), (Region, " co "));

        Assert.Equal("google", options.Provider);
        Assert.Equal(FakeKey, options.GoogleApiKey);
        Assert.Equal("es-419", options.GoogleLanguage);
        Assert.Equal("CO", options.GoogleRegion);
        Assert.Equal("es-419", options.EffectiveGoogleLanguage);
        Assert.Equal("CO", options.EffectiveGoogleRegion);
        Assert.Empty(Failures((Provider, " Google "), (Key, FakeKey + "\n"), (Language, " es-419 "), (Region, " co ")));
    }

    [Fact]
    public void The_numbers_bind_from_their_variables()
    {
        var options = Bind((PerMinute, "120"), (PerDay, "5000"));

        Assert.Equal((120, 5000), (options.TilesPerMinute, options.TilesPerUserPerDay));
    }

    // Provider

    [Theory]
    [InlineData("osm")]
    [InlineData("google")]
    [InlineData("none")]
    public void The_three_providers_are_accepted(string provider) =>
        Assert.Empty(Failures((Provider, provider), (Key, FakeKey)));

    [Fact]
    public void Unknown_provider_names_the_variable_and_the_accepted_values()
    {
        var failures = Failures((Provider, "mapbox"));

        var failure = Assert.Single(failures);
        Assert.Contains(Provider, failure);
        Assert.Contains("none, osm or google", failure);
        Assert.Contains("mapbox", failure);
    }

    [Fact]
    public void Google_without_a_key_names_the_key_variable()
    {
        var failure = Assert.Single(Failures((Provider, "google")));

        Assert.Contains(Key, failure);
    }

    [Fact]
    public void A_key_that_is_not_shaped_like_a_google_key_is_refused_without_repeating_it()
    {
        var bad = "not a key&session=x";

        var failure = Assert.Single(Failures((Provider, "google"), (Key, bad)));

        Assert.Contains(Key, failure);
        Assert.DoesNotContain(bad, failure);
    }

    [Fact]
    public void A_key_is_not_checked_when_the_provider_is_not_google()
    {
        Assert.Empty(Failures((Provider, "osm"), (Key, "not a key")));
        Assert.Empty(Failures((Key, "not a key")));
    }

    [Theory]
    [InlineData("es-419", true)]
    [InlineData("en-US", true)]
    [InlineData("es", true)]
    [InlineData("en_US", false)]
    [InlineData("english", false)]
    [InlineData("e", false)]
    public void The_language_is_an_ietf_tag_when_the_provider_is_google(string language, bool valid)
    {
        var failures = Failures((Provider, "google"), (Key, FakeKey), (Language, language));

        Assert.Equal(valid, failures.Length == 0);
        if (!valid)
        {
            Assert.Contains(Language, Assert.Single(failures));
        }
    }

    [Theory]
    [InlineData("CO", true)]
    [InlineData("co", true)]
    [InlineData("419", true)]
    [InlineData("COL", false)]
    [InlineData("C", false)]
    public void The_region_is_a_cldr_code_when_the_provider_is_google(string region, bool valid)
    {
        var failures = Failures((Provider, "google"), (Key, FakeKey), (Region, region));

        Assert.Equal(valid, failures.Length == 0);
        if (!valid)
        {
            Assert.Contains(Region, Assert.Single(failures));
        }
    }

    // Ranges

    [Theory]
    [InlineData(59, false)]
    [InlineData(60, true)]
    [InlineData(10_000, true)]
    [InlineData(10_001, false)]
    public void The_tiles_per_minute_is_between_60_and_10000(int value, bool valid)
    {
        var failures = Failures((PerMinute, value.ToString()));

        Assert.Equal(valid, failures.Length == 0);
        if (!valid)
        {
            Assert.Contains(PerMinute, Assert.Single(failures));
        }
    }

    [Theory]
    [InlineData(99, false)]
    [InlineData(100, true)]
    [InlineData(1_000_000, true)]
    [InlineData(1_000_001, false)]
    public void The_tiles_per_day_is_between_100_and_1000000(int value, bool valid)
    {
        var failures = Failures((PerDay, value.ToString()));

        Assert.Equal(valid, failures.Length == 0);
        if (!valid)
        {
            Assert.Contains(PerDay, Assert.Single(failures));
        }
    }

    [Fact]
    public void Every_failure_is_reported_at_once()
    {
        var failures = Failures((Provider, "google"), (PerMinute, "1"), (PerDay, "1"));

        Assert.Equal(3, failures.Length);
        Assert.Contains(failures, f => f.Contains(Key));
        Assert.Contains(failures, f => f.Contains(PerMinute));
        Assert.Contains(failures, f => f.Contains(PerDay));
    }

    // Startup line

    [Fact]
    public async Task With_no_provider_the_log_says_no_request_is_made_and_nothing_warns()
    {
        var lines = await StartupLogAsync();

        var line = Assert.Single(lines);
        Assert.Equal(LogLevel.Information, line.Level);
        Assert.Contains("no request is made to any tile provider", line.Message);
    }

    [Fact]
    public async Task The_log_names_the_hosts_that_will_be_contacted_and_by_whom()
    {
        var osm = Assert.Single(await StartupLogAsync((Provider, "osm")));
        var google = Assert.Single(await StartupLogAsync((Provider, "google"), (Key, FakeKey)));

        Assert.Contains("browsers will contact tile.openstreetmap.org", osm.Message);
        Assert.Contains("api will contact tile.googleapis.com", google.Message);
        Assert.DoesNotContain(FakeKey, google.Message);
    }

    [Fact]
    public async Task A_malformed_language_with_provider_none_starts_with_one_warning_naming_the_variable_and_not_its_value()
    {
        var lines = await StartupLogAsync((Language, "english_US"), (Key, FakeKey));

        Assert.Equal(2, lines.Count);
        var warning = Assert.Single(lines, l => l.Level == LogLevel.Warning);
        Assert.Contains(Language, warning.Message);
        Assert.Contains(Key, warning.Message);
        Assert.Contains("ignored", warning.Message);
        Assert.DoesNotContain(FakeKey, warning.Message);
        Assert.DoesNotContain("english_US", warning.Message);
        Assert.Empty(Failures((Language, "english_US")));
    }

    [Fact]
    public async Task Google_variables_with_google_selected_draw_no_warning()
    {
        var lines = await StartupLogAsync((Provider, "google"), (Key, FakeKey), (Language, "es-419"), (Region, "CO"));

        Assert.DoesNotContain(lines, l => l.Level == LogLevel.Warning);
    }
}
