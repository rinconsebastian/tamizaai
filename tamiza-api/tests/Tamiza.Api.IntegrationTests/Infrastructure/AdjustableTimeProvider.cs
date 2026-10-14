namespace Tamiza.Api.IntegrationTests.Infrastructure;

/// <summary>The system clock plus an offset a test moves forward, so retry delays can be crossed without waiting.</summary>
public sealed class AdjustableTimeProvider : TimeProvider
{
    private long _offsetTicks;

    public void Advance(TimeSpan by) => Interlocked.Add(ref _offsetTicks, by.Ticks);

    public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + TimeSpan.FromTicks(Interlocked.Read(ref _offsetTicks));
}
