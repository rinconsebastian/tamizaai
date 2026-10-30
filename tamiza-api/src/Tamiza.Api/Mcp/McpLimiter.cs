using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace Tamiza.Api.Mcp;

/// <summary>
/// The limits of one person across all their clients (design D12): calls at the same time and calls in the last minute. In memory,
/// because one <c>api</c> instance runs, as the ingestion queue and the assistant already assume. A call refused for a limit is not counted.
/// </summary>
public sealed class McpLimiter(IOptions<McpOptions> options, TimeProvider time)
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    /// <summary>The wait suggested to a client over the concurrency limit: its other calls are about to end.</summary>
    private const int ConcurrencyRetrySeconds = 2;

    private readonly ConcurrentDictionary<Guid, PersonState> _people = new();

    /// <summary>
    /// Takes a place for a call of <paramref name="userId"/>; dispose it when the call ends. Returns null, with the seconds to wait,
    /// when the person is over a limit.
    /// </summary>
    public IDisposable? TryEnter(Guid userId, out int retryAfterSeconds)
    {
        var settings = options.Value;
        var state = _people.GetOrAdd(userId, _ => new PersonState());
        var now = time.GetUtcNow();
        lock (state)
        {
            while (state.Starts.Count > 0 && now - state.Starts.Peek() >= Window)
            {
                state.Starts.Dequeue();
            }

            if (state.Starts.Count >= settings.CallsPerMinutePerUser)
            {
                retryAfterSeconds = Math.Max(1, (int)Math.Ceiling((Window - (now - state.Starts.Peek())).TotalSeconds));
                return null;
            }

            if (state.Running >= settings.MaxConcurrentCallsPerUser)
            {
                retryAfterSeconds = ConcurrencyRetrySeconds;
                return null;
            }

            state.Starts.Enqueue(now);
            state.Running++;
        }

        retryAfterSeconds = 0;
        return new Slot(state);
    }

    /// <summary>
    /// Whether the refusal of a call for a limit should be written to the records: once a minute for a person, so a client that keeps calling
    /// past its limit cannot grow the table by a row for every attempt. The refusal itself is always answered.
    /// </summary>
    public bool ShouldRecordRefusal(Guid userId)
    {
        var state = _people.GetOrAdd(userId, _ => new PersonState());
        var now = time.GetUtcNow();
        lock (state)
        {
            if (state.LastRefusalRecorded is { } last && now - last < Window)
            {
                return false;
            }

            state.LastRefusalRecorded = now;
            return true;
        }
    }

    private sealed class PersonState
    {
        public Queue<DateTimeOffset> Starts { get; } = new();

        public DateTimeOffset? LastRefusalRecorded { get; set; }

        public int Running { get; set; }
    }

    private sealed class Slot(PersonState state) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                lock (state)
                {
                    state.Running--;
                }
            }
        }
    }
}
