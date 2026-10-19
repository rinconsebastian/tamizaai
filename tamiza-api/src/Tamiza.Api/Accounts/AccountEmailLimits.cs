using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace Tamiza.Api.Accounts;

/// <summary>Whether an account email may go now; <see cref="RetryAfterSeconds"/> is how long to wait when not.</summary>
public readonly record struct EmailLimitDecision(bool Allowed, int RetryAfterSeconds)
{
    public static readonly EmailLimitDecision Yes = new(true, 0);
}

/// <summary>
/// What limits account emails (design D8): an hourly allowance per requesting user (<see cref="AccountEmailOptions.PerHour"/>), a
/// minimum interval between two emails to the same account, and striped locks so one person is handled by one request at a time.
/// One api instance is assumed, as everywhere in the MVP: a restart resets the allowance and the intervals.
/// </summary>
public sealed class AccountEmailLimits : IDisposable
{
    /// <summary>The same account is sent at most one account email per this time.</summary>
    public static readonly TimeSpan AccountInterval = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan Window = TimeSpan.FromHours(1);

    private const int Stripes = 32;
    private const int PruneAbove = 1000;

    private readonly int _perHour;
    private readonly TimeProvider _time;
    private readonly object _windowGate = new();
    private readonly Dictionary<Guid, (DateTimeOffset Start, int Used)> _windows = [];
    private readonly ConcurrentDictionary<string, DateTimeOffset> _sentUntil = new();
    private readonly SemaphoreSlim[] _locks = Enumerable.Range(0, Stripes).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    public AccountEmailLimits(IOptions<AccountEmailOptions> options, TimeProvider time)
    {
        _time = time;
        _perHour = options.Value.PerHour;
    }

    /// <summary>Whether the user has allowance left, without using any. Lets a request stop before it calls the provider.</summary>
    public EmailLimitDecision Peek(Guid userId)
    {
        lock (_windowGate)
        {
            return Check(userId, out _);
        }
    }

    /// <summary>Takes one email from the user's hourly allowance. The hour runs from the user's first email in it.</summary>
    public EmailLimitDecision TryTake(Guid userId)
    {
        lock (_windowGate)
        {
            var decision = Check(userId, out var window);
            if (_windows.Count > PruneAbove)
            {
                var cutoff = _time.GetUtcNow() - Window;
                foreach (var (user, stale) in _windows.Where(w => w.Value.Start <= cutoff).ToList())
                {
                    _windows.Remove(user);
                }
            }

            if (decision.Allowed)
            {
                _windows[userId] = (window.Start, window.Used + 1);
            }

            return decision;
        }
    }

    // Callers hold _windowGate. A user whose last hour has passed starts a new one.
    private EmailLimitDecision Check(Guid userId, out (DateTimeOffset Start, int Used) window)
    {
        var now = _time.GetUtcNow();
        if (!_windows.TryGetValue(userId, out window) || now - window.Start >= Window)
        {
            window = (now, 0);
        }

        if (window.Used < _perHour)
        {
            return EmailLimitDecision.Yes;
        }

        var wait = (int)Math.Ceiling((window.Start + Window - now).TotalSeconds);
        return new EmailLimitDecision(false, Math.Clamp(wait, 1, (int)Window.TotalSeconds));
    }

    /// <summary>Seconds until the account may be sent another email; 0 when it may be sent one now.</summary>
    public int IntervalRemaining(string accountId)
    {
        if (!_sentUntil.TryGetValue(accountId, out var until))
        {
            return 0;
        }

        var remaining = until - _time.GetUtcNow();
        return remaining > TimeSpan.Zero ? (int)Math.Ceiling(remaining.TotalSeconds) : 0;
    }

    /// <summary>Starts the interval after an email was requested for the account.</summary>
    public void MarkSent(string accountId)
    {
        var now = _time.GetUtcNow();
        _sentUntil[accountId] = now + AccountInterval;
        if (_sentUntil.Count > PruneAbove)
        {
            foreach (var (key, until) in _sentUntil)
            {
                if (until <= now)
                {
                    _sentUntil.TryRemove(key, out _);
                }
            }
        }
    }

    /// <summary>Waits for the lock of the stripe the key falls in, so two requests for one person cannot interleave.</summary>
    public async Task<IDisposable> LockAsync(string key, CancellationToken cancellationToken)
    {
        var gate = _locks[(int)((uint)StringComparer.Ordinal.GetHashCode(key) % Stripes)];
        await gate.WaitAsync(cancellationToken);
        return new Releaser(gate);
    }

    public void Dispose()
    {
        foreach (var gate in _locks)
        {
            gate.Dispose();
        }
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                gate.Release();
            }
        }
    }
}
