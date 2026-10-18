using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace Tamiza.Api.Maps;

/// <summary>Whether a user may have one more tile (or credits); <see cref="RetryAfterSeconds"/> is how long to wait when not.</summary>
public readonly record struct LimitDecision(bool Allowed, int RetryAfterSeconds)
{
    public static readonly LimitDecision Yes = new(true, 0);
}

/// <summary>
/// What one user may take from Google through the api (design D15): tiles per minute (<see cref="MapOptions.TilesPerMinute"/>) and
/// per UTC day (<see cref="MapOptions.TilesPerUserPerDay"/>), and, separately, credits requests per minute, so fast panning never
/// costs the credits. One api instance is assumed, as everywhere in the MVP: a restart resets the counters.
/// </summary>
public sealed class TileLimits : IDisposable
{
    private readonly PartitionedRateLimiter<Guid> _tilesPerMinute;
    private readonly PartitionedRateLimiter<Guid> _creditsPerMinute;
    private readonly TimeProvider _time;
    private readonly int _perDay;
    private readonly object _dayGate = new();
    private readonly Dictionary<Guid, int> _today = [];
    private DateOnly _day;

    public TileLimits(IOptions<MapOptions> options, TimeProvider time)
    {
        var settings = options.Value;
        _time = time;
        _perDay = settings.TilesPerUserPerDay;
        _tilesPerMinute = PerMinute(settings.TilesPerMinute);
        _creditsPerMinute = PerMinute(settings.CreditsPerMinute);
    }

    private static PartitionedRateLimiter<Guid> PerMinute(int limit) =>
        PartitionedRateLimiter.Create<Guid, Guid>(user => RateLimitPartition.GetFixedWindowLimiter(user, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = limit,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true,
        }));

    /// <summary>Takes one tile from the user's minute and day allowances. The minute is asked first, so a refused minute spends no day.</summary>
    public LimitDecision TryTile(Guid userId)
    {
        var minute = TryMinute(_tilesPerMinute, userId);
        return minute.Allowed ? TryDay(userId) : minute;
    }

    /// <summary>Takes one credits request from the user's own minute allowance.</summary>
    public LimitDecision TryCredits(Guid userId) => TryMinute(_creditsPerMinute, userId);

    private static LimitDecision TryMinute(PartitionedRateLimiter<Guid> limiter, Guid userId)
    {
        using var lease = limiter.AttemptAcquire(userId);
        if (lease.IsAcquired)
        {
            return LimitDecision.Yes;
        }

        var wait = lease.TryGetMetadata(MetadataName.RetryAfter, out var after) ? (int)Math.Ceiling(after.TotalSeconds) : 60;
        return new LimitDecision(false, Math.Clamp(wait, 1, 60));
    }

    private LimitDecision TryDay(Guid userId)
    {
        var now = _time.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        lock (_dayGate)
        {
            if (today != _day)
            {
                _today.Clear();
                _day = today;
            }

            _today.TryGetValue(userId, out var used);
            if (used >= _perDay)
            {
                var midnight = new DateTimeOffset(today.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
                return new LimitDecision(false, Math.Max(1, (int)Math.Ceiling((midnight - now).TotalSeconds)));
            }

            _today[userId] = used + 1;
            return LimitDecision.Yes;
        }
    }

    public void Dispose()
    {
        _tilesPerMinute.Dispose();
        _creditsPerMinute.Dispose();
    }
}
