using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Tamiza.Api.Maps;

/// <summary>
/// Settings of the optional basemaps under the dashboard's maps, read from the flat <c>TAMIZA_MAP_*</c> variables. Compose always
/// passes optional strings, often empty, so a blank value means unset. Text values are trimmed. A deployment offers one provider
/// (<see cref="ProviderNone"/>, <see cref="ProviderOsm"/> or <see cref="ProviderGoogle"/>): with none, nobody makes a request to a tile provider.
/// </summary>
public sealed partial class MapOptions
{
    public const string ProviderVariable = "TAMIZA_MAP_BASEMAP_PROVIDER";
    public const string GoogleApiKeyVariable = "TAMIZA_MAP_GOOGLE_API_KEY";
    public const string GoogleLanguageVariable = "TAMIZA_MAP_GOOGLE_LANGUAGE";
    public const string GoogleRegionVariable = "TAMIZA_MAP_GOOGLE_REGION";
    public const string TilesPerMinuteVariable = "TAMIZA_MAP_TILES_PER_MINUTE";
    public const string TilesPerUserPerDayVariable = "TAMIZA_MAP_TILES_PER_USER_PER_DAY";

    public const string ProviderNone = "none";
    public const string ProviderOsm = "osm";
    public const string ProviderGoogle = "google";

    public const int DefaultTilesPerMinute = 600;
    public const int DefaultTilesPerUserPerDay = 3000;

    /// <summary>What Google gets when the operator sets no language or region: the session needs both.</summary>
    public const string DefaultGoogleLanguage = "en-US";

    public const string DefaultGoogleRegion = "US";

    private string _provider = "";
    private string _googleApiKey = "";
    private string _googleLanguage = "";
    private string _googleRegion = "";

    /// <summary><c>none</c>, <c>osm</c> or <c>google</c>; blank means <c>none</c>. Lower-cased, so <c>Google</c> works.</summary>
    [ConfigurationKeyName(ProviderVariable)]
    public string Provider { get => _provider; set => _provider = value?.Trim().ToLowerInvariant() ?? ""; }

    /// <summary>A Map Tiles API key. Never logged, never returned, never sent to a browser.</summary>
    [ConfigurationKeyName(GoogleApiKeyVariable)]
    public string GoogleApiKey { get => _googleApiKey; set => _googleApiKey = value?.Trim() ?? ""; }

    /// <summary>An IETF language tag for Google's labels, such as <c>es-419</c>.</summary>
    [ConfigurationKeyName(GoogleLanguageVariable)]
    public string GoogleLanguage { get => _googleLanguage; set => _googleLanguage = value?.Trim() ?? ""; }

    /// <summary>A CLDR region code, such as <c>CO</c>. Upper-cased.</summary>
    [ConfigurationKeyName(GoogleRegionVariable)]
    public string GoogleRegion { get => _googleRegion; set => _googleRegion = value?.Trim().ToUpperInvariant() ?? ""; }

    [ConfigurationKeyName(TilesPerMinuteVariable)]
    public int TilesPerMinute { get; set; } = DefaultTilesPerMinute;

    [ConfigurationKeyName(TilesPerUserPerDayVariable)]
    public int TilesPerUserPerDay { get; set; } = DefaultTilesPerUserPerDay;

    // Not bound to any variable (design D12): tests set them through the host.

    /// <summary>How long one tile link is the same string for a user and a basemap: clock-aligned windows of this length.</summary>
    public TimeSpan TileLinkWindow { get; set; } = TimeSpan.FromMinutes(45);

    /// <summary>How long a link keeps working after its window ends, so a late timer or a clock that is off does not break the map.</summary>
    public TimeSpan TileLinkGrace { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Attribution requests (<c>GET /basemaps/{kind}/credits</c>) per user per minute, separate from the tile limit.</summary>
    public int CreditsPerMinute { get; set; } = 120;

    /// <summary>The effective provider: <see cref="ProviderNone"/> when blank.</summary>
    public string EffectiveProvider => Provider.Length == 0 ? ProviderNone : Provider;

    public bool IsGoogle => EffectiveProvider == ProviderGoogle;

    public bool IsOsm => EffectiveProvider == ProviderOsm;

    public string EffectiveGoogleLanguage => GoogleLanguage.Length == 0 ? DefaultGoogleLanguage : GoogleLanguage;

    public string EffectiveGoogleRegion => GoogleRegion.Length == 0 ? DefaultGoogleRegion : GoogleRegion;

    /// <summary>The names of the Google variables that are set, for the warning about settings that have no effect.</summary>
    public IReadOnlyList<string> GoogleVariablesSet()
    {
        var set = new List<string>();
        if (GoogleApiKey.Length > 0)
        {
            set.Add(GoogleApiKeyVariable);
        }

        if (GoogleLanguage.Length > 0)
        {
            set.Add(GoogleLanguageVariable);
        }

        if (GoogleRegion.Length > 0)
        {
            set.Add(GoogleRegionVariable);
        }

        return set;
    }

    /// <summary>Google keys are letters, digits, <c>-</c> and <c>_</c>; anything else would not be a key and could alter the request's address.</summary>
    [GeneratedRegex(@"^[A-Za-z0-9_-]{1,256}$")]
    internal static partial Regex KeyShape();

    [GeneratedRegex(@"^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8}){0,3}$")]
    internal static partial Regex LanguageShape();

    [GeneratedRegex(@"^([A-Z]{2}|[0-9]{3})$")]
    internal static partial Regex RegionShape();
}

/// <summary>
/// Stops the api at startup, naming the variable, for values that are typos the operator must see at deploy time. Settings of a
/// provider that is not selected are not checked: they only draw a warning (<see cref="MapStartupCheck"/>).
/// </summary>
public sealed class MapOptionsValidator : IValidateOptions<MapOptions>
{
    public ValidateOptionsResult Validate(string? name, MapOptions options)
    {
        var failures = new List<string>();

        if (options.EffectiveProvider is not (MapOptions.ProviderNone or MapOptions.ProviderOsm or MapOptions.ProviderGoogle))
        {
            failures.Add(
                $"{MapOptions.ProviderVariable} must be {MapOptions.ProviderNone}, {MapOptions.ProviderOsm} or {MapOptions.ProviderGoogle}, not '{options.Provider}'.");
        }

        if (options.IsGoogle)
        {
            // The key is never repeated in a message.
            if (options.GoogleApiKey.Length == 0)
            {
                failures.Add($"{MapOptions.GoogleApiKeyVariable} is required when {MapOptions.ProviderVariable} is {MapOptions.ProviderGoogle}.");
            }
            else if (!MapOptions.KeyShape().IsMatch(options.GoogleApiKey))
            {
                failures.Add($"{MapOptions.GoogleApiKeyVariable} is not a Google API key: it has letters, digits, '-' and '_' only.");
            }

            if (options.GoogleLanguage.Length > 0 && !MapOptions.LanguageShape().IsMatch(options.GoogleLanguage))
            {
                failures.Add($"{MapOptions.GoogleLanguageVariable} must be an IETF language tag such as es-419 or en-US, not '{options.GoogleLanguage}'.");
            }

            if (options.GoogleRegion.Length > 0 && !MapOptions.RegionShape().IsMatch(options.GoogleRegion))
            {
                failures.Add($"{MapOptions.GoogleRegionVariable} must be a region code such as CO or US, not '{options.GoogleRegion}'.");
            }
        }

        CheckRange(failures, MapOptions.TilesPerMinuteVariable, options.TilesPerMinute, 60, 10_000);
        CheckRange(failures, MapOptions.TilesPerUserPerDayVariable, options.TilesPerUserPerDay, 100, 1_000_000);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void CheckRange(List<string> failures, string variable, int value, int min, int max)
    {
        if (value < min || value > max)
        {
            failures.Add($"{variable} must be between {min} and {max}, not {value}.");
        }
    }
}
