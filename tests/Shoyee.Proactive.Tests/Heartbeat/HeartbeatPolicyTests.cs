using Shoyee.Contracts.Options;
using Shoyee.Proactive.Heartbeat;
using Xunit;

namespace Shoyee.Proactive.Tests.Heartbeat;

public sealed class HeartbeatPolicyTests
{
    private static readonly TimeOnly Noon = new(14, 0);

    [Fact]
    public void Disabled_DoesNothing()
    {
        var decision = HeartbeatPolicy.Decide(Noon, 600, 80, Options(enabled: false), _ => 0);

        Assert.Equal(HeartbeatZone.Disabled, decision.Zone);
        Assert.False(decision.Triggered);
        Assert.Equal(80, decision.Longing);
    }

    [Theory]
    [InlineData(23, 0, true)]   // 起点含
    [InlineData(23, 59, true)]
    [InlineData(3, 0, true)]
    [InlineData(6, 29, true)]
    [InlineData(6, 30, false)]  // 终点不含
    [InlineData(12, 0, false)]
    public void QuietHours_CrossesMidnight(int hour, int minute, bool expected)
    {
        Assert.Equal(expected, HeartbeatPolicy.IsQuietHours(new TimeOnly(hour, minute), Options()));
    }

    [Fact]
    public void QuietHours_SameStartAndEnd_MeansDisabled()
    {
        var options = Options() with { QuietHoursStart = new TimeOnly(0, 0), QuietHoursEnd = new TimeOnly(0, 0) };
        Assert.False(HeartbeatPolicy.IsQuietHours(new TimeOnly(3, 0), options));
    }

    [Fact]
    public void QuietHours_DoesNotTouchLonging()
    {
        var decision = HeartbeatPolicy.Decide(new TimeOnly(23, 30), 600, 50, Options(), _ => 0);

        Assert.Equal(HeartbeatZone.QuietHours, decision.Zone);
        Assert.False(decision.Triggered);
        Assert.Equal(50, decision.Longing);
    }

    [Fact]
    public void HotZone_DoesNotTouchLonging()
    {
        // 闲置 10 分钟 < 炽热区 20
        var decision = HeartbeatPolicy.Decide(Noon, 10, 50, Options(), _ => 0);

        Assert.Equal(HeartbeatZone.Hot, decision.Zone);
        Assert.False(decision.Triggered);
        Assert.Equal(50, decision.Longing);
    }

    [Fact]
    public void WarmZone_IncreasesLongingButNeverTriggers()
    {
        // 闲置 25 分钟：>=20 且 <30；增量 = 1 + 3 = 4
        var next = Sequence(3);

        var decision = HeartbeatPolicy.Decide(Noon, 25, 50, Options(), next);

        Assert.Equal(HeartbeatZone.Warm, decision.Zone);
        Assert.False(decision.Triggered);
        Assert.Equal(54, decision.Longing);
    }

    [Fact]
    public void ColdZone_BelowProbability_NoTrigger()
    {
        // 增量拉满 7，思念 7，掷出 100 → 100 > 7，不触发
        var next = Sequence(6, 99);

        var decision = HeartbeatPolicy.Decide(Noon, 60, 0, Options(), next);

        Assert.Equal(HeartbeatZone.Cold, decision.Zone);
        Assert.False(decision.Triggered);
        Assert.Equal(7, decision.Longing);
    }

    [Fact]
    public void ColdZone_FullLonging_AlwaysTriggers_AndPaysPenalty()
    {
        var options = Options() with { LevelFluctuationChance = 0 };
        var next = Sequence(0, 0, 0); // 增量 +1（封顶 100）→ 掷 1 → 触发 → 类别 text

        var decision = HeartbeatPolicy.Decide(Noon, 60, 100, options, next);

        Assert.Equal(HeartbeatZone.Cold, decision.Zone);
        Assert.True(decision.Triggered);
        Assert.Equal(50, decision.Longing);
        Assert.Equal(4, decision.Level);       // 100 ≥ 70 → 第 5 档
        Assert.Equal("text", decision.PromptType);
    }

    [Theory]
    [InlineData(14, 0)]
    [InlineData(15, 1)]
    [InlineData(29, 1)]
    [InlineData(30, 2)]
    [InlineData(49, 2)]
    [InlineData(50, 3)]
    [InlineData(69, 3)]
    [InlineData(70, 4)]
    public void Level_FollowsHaremThresholds(int longing, int expectedLevel)
    {
        var options = Options() with
        {
            LevelFluctuationChance = 0,
            NormalIncreaseMin = 0,
            NormalIncreaseMax = 0,
        };

        var decision = HeartbeatPolicy.Decide(Noon, 60, longing, options, Sequence(0, 0, 0));

        Assert.True(decision.Triggered);
        Assert.Equal(expectedLevel, decision.Level);
    }

    [Fact]
    public void LevelFluctuation_CanMoveOneStepDown()
    {
        var options = Options() with { LevelFluctuationChance = 100 };
        // 增量 0 → 思念 70（第 5 档）→ 掷 0 / 触发 → 波动 0<100 → 硬币 1 → 退一档到 3
        var decision = HeartbeatPolicy.Decide(Noon, 60, 70, options, Sequence(0, 0, 0, 1, 0));

        Assert.True(decision.Triggered);
        Assert.Equal(3, decision.Level);
    }

    [Fact]
    public void ColdZone_BelowTriggerThreshold_NeverTriggers()
    {
        // 思念 79 < 阈值 80：即使增量拉满、掷骰子必中也只能涨到 79
        var options = Options() with
        {
            TriggerThreshold = 80,
            NormalIncreaseMin = 0,
            NormalIncreaseMax = 0,
        };

        var decision = HeartbeatPolicy.Decide(Noon, 60, 79, options, _ => 0);

        Assert.Equal(HeartbeatZone.Cold, decision.Zone);
        Assert.False(decision.Triggered);
        Assert.Equal(79, decision.Longing);
    }

    [Fact]
    public void ColdZone_AtTriggerThreshold_CanTrigger()
    {
        var options = Options() with
        {
            TriggerThreshold = 80,
            NormalIncreaseMin = 0,
            NormalIncreaseMax = 0,
            LevelFluctuationChance = 0,
        };

        var decision = HeartbeatPolicy.Decide(Noon, 60, 80, options, Sequence(0, 0, 0));

        Assert.True(decision.Triggered);
        Assert.Equal(30, decision.Longing); // 80 - 50
    }

    [Fact]
    public void Longing_IsClampedToCap()
    {
        var options = Options() with { LongingCap = 20, NormalIncreaseMin = 50, NormalIncreaseMax = 50 };
        var decision = HeartbeatPolicy.Decide(Noon, 60, 10, options, Sequence(0, 99));

        Assert.Equal(20, decision.Longing);
    }

    private static HeartbeatOptions Options(bool enabled = true)
        => new(
            Enabled: enabled,
            MinMinutes: 45,
            MaxMinutes: 75,
            CooldownMinutes: 30,
            HotZoneMinutes: 20,
            LongingCap: 100,
            NormalIncreaseMin: 1,
            NormalIncreaseMax: 7,
            TriggerPenalty: 50,
            TriggerThreshold: 0,
            LevelFluctuationChance: 0,
            QuietHoursStart: new TimeOnly(23, 0),
            QuietHoursEnd: new TimeOnly(6, 30));

    /// <summary>按顺序返回给定值；用完后一直返回 0。</summary>
    private static Func<int, int> Sequence(params int[] values)
    {
        var index = 0;
        return _ => index < values.Length ? values[index++] : 0;
    }
}
