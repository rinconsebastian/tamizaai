using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Tamiza.Api.Indicators;

namespace Tamiza.Api.Maps;

/// <summary>A tile link as given to a browser and when it stops working; <see cref="WindowEnd"/> is when the next list will hold a new one.</summary>
public sealed record IssuedTileLink(string Link, DateTimeOffset ExpiresAt, DateTimeOffset WindowEnd);

/// <summary>Whom a tile link was issued to and for which basemap.</summary>
public sealed record TileLinkClaims(Guid UserId, string Kind);

/// <summary>
/// The links that give a browser Google tiles (design D15). A link is the Data Protection payload <c>userId|kind|windowStart</c>, a
/// plain protector with its own purpose, so it cannot be forged and carries nothing readable. Time is cut into clock-aligned windows;
/// within one, a user gets the same string for a basemap every time (memoized), so the browser's cache, keyed by address, can
/// reuse tiles. A link works until its window ends plus a grace, checked here with the injected <see cref="TimeProvider"/>:
/// <c>ITimeLimitedDataProtector</c> checks the real clock, which tests cannot move. No link outlives an hour.
/// </summary>
public sealed class TileLinks(IDataProtectionProvider protection, IMemoryCache cache, TimeProvider time, IOptions<MapOptions> options)
{
    public const string Purpose = "Tamiza.Maps.TileLink.v1";

    private readonly IDataProtector _protector = protection.CreateProtector(Purpose);
    private readonly object _gate = new();

    private sealed record MemoKey(Guid UserId, string Kind, long WindowStart);

    /// <summary>The user's link for the basemap in the current window; the same string until the window ends.</summary>
    public IssuedTileLink Issue(Guid userId, string kind)
    {
        var settings = options.Value;
        var now = time.GetUtcNow();
        var windowSeconds = (long)settings.TileLinkWindow.TotalSeconds;
        var start = Math.DivRem(now.ToUnixTimeSeconds(), windowSeconds, out _) * windowSeconds;
        var windowEnd = DateTimeOffset.FromUnixTimeSeconds(start + windowSeconds);
        var expiresAt = windowEnd + settings.TileLinkGrace;

        lock (_gate)
        {
            var key = new MemoKey(userId, kind, start);
            if (!cache.TryGetValue(key, out string? link) || link is null)
            {
                link = _protector.Protect(string.Create(CultureInfo.InvariantCulture, $"{userId:N}|{kind}|{start}"));
                // Relative to now, as the injected clock sees it: the cache keeps its own time, which a test clock does not move.
                cache.Set(key, link, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = expiresAt - now });
            }

            return new IssuedTileLink(link, expiresAt, windowEnd);
        }
    }

    /// <summary>The claims of a link this api issued that has not expired, or null for anything else.</summary>
    public TileLinkClaims? Read(string link)
    {
        string payload;
        try
        {
            payload = _protector.Unprotect(link);
        }
        catch (CryptographicException)
        {
            return null;
        }

        var parts = payload.Split('|');
        if (parts.Length != 3 || !Guid.TryParseExact(parts[0], "N", out var userId) || !BasemapKinds.All.Contains(parts[1])
            || !long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var start))
        {
            return null;
        }

        var settings = options.Value;
        var end = DateTimeOffset.FromUnixTimeSeconds(start + (long)settings.TileLinkWindow.TotalSeconds) + settings.TileLinkGrace;
        return time.GetUtcNow() < end ? new TileLinkClaims(userId, parts[1]) : null;
    }
}
