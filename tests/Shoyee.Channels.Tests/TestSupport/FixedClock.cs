namespace Shoyee.Channels.Tests.TestSupport;

/// <summary>可推进的固定时钟（退避/补发时间可预期）。</summary>
internal sealed class FixedClock : TimeProvider
{
    private DateTimeOffset _now;

    public FixedClock(DateTimeOffset now) => _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan delta) => _now += delta;
}
