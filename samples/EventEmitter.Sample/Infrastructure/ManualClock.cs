namespace Codefinity.EventEmitter.Sample.Infrastructure;

/// <summary>
/// A <see cref="TimeProvider"/> the example moves by hand, to show time-based features without waiting.
/// Register it before <c>AddEventEmitter()</c>.
/// </summary>
internal sealed class ManualClock : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.UtcNow;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
