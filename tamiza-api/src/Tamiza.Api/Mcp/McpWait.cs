using ModelContextProtocol;

namespace Tamiza.Api.Mcp;

/// <summary>
/// Waits for a test or a run inside one tool call (design D10): polls once a second until it ends or the time given passes, and sends a
/// progress notification every five seconds when the client asked for them. Cancelling the call ends the wait only; the test or run goes on.
/// </summary>
public static class McpWait
{
    public const int MaxSeconds = 120;
    public const int DefaultSeconds = 50;
    private static readonly TimeSpan PollEvery = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ProgressEvery = TimeSpan.FromSeconds(5);

    /// <summary>The seconds to wait: <see cref="DefaultSeconds"/> when omitted, and never outside 0 to <see cref="MaxSeconds"/>.</summary>
    public static int Seconds(int? requested) => Math.Clamp(requested ?? DefaultSeconds, 0, MaxSeconds);

    /// <summary>
    /// Calls <paramref name="read"/> until it reports that the work ended or <paramref name="seconds"/> pass, and returns the last value.
    /// <paramref name="read"/> returns the state and whether the work is over, with a short message for the progress notification.
    /// </summary>
    public static async Task<T> UntilAsync<T>(
        Func<CancellationToken, Task<(T State, bool Done, string Message)>> read, int seconds, IProgress<ProgressNotificationValue>? progress, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(seconds);
        var lastProgress = DateTimeOffset.MinValue;
        var step = 0f;
        while (true)
        {
            var (state, done, message) = await read(ct);
            if (done || DateTimeOffset.UtcNow >= deadline)
            {
                return state;
            }

            if (progress is not null && DateTimeOffset.UtcNow - lastProgress >= ProgressEvery)
            {
                lastProgress = DateTimeOffset.UtcNow;
                progress.Report(new ProgressNotificationValue { Progress = ++step, Message = message });
            }

            try
            {
                await Task.Delay(PollEvery, ct);
            }
            catch (OperationCanceledException)
            {
                // The call was cancelled or the client left: answer with what is known. The test or run is not touched.
                return state;
            }
        }
    }
}
