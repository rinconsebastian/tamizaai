namespace Tamiza.Api.IntegrationTests.Infrastructure;

/// <summary>A clock a test sets to a given moment, such as Monday 07:00 in Bogotá, and moves by hand.</summary>
public sealed class SettableTimeProvider(DateTimeOffset start) : TimeProvider
{
    private long _ticks = start.UtcTicks;

    public void Set(DateTimeOffset moment) => Interlocked.Exchange(ref _ticks, moment.UtcTicks);

    public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);

    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);
}
