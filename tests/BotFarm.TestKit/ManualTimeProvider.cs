namespace BotFarm.TestKit;

/// <summary>
/// A <see cref="TimeProvider"/> whose current time only changes when a test advances it.
/// </summary>
public sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;

    public ManualTimeProvider(DateTimeOffset? start = null)
    {
        _utcNow = start ?? new DateTimeOffset(2024, 01, 01, 0, 0, 0, TimeSpan.Zero);
    }

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan delta) => _utcNow += delta;
}
