using Shoyee.Agent.Composition;
using Shoyee.Agent.Tests.TestSupport;
using Shoyee.Contracts.Options;
using Xunit;

namespace Shoyee.Agent.Tests.Composition;

public sealed class ContextCompactionPolicyTests
{
    [Fact]
    public void Default_IsDisabled()
    {
        var plan = ContextCompactionPolicy.Resolve(null);

        Assert.Null(plan);
        Assert.Null(ContextCompactionPolicy.Create(null, new RecordingChatClient()));
    }

    [Fact]
    public void Enabled_DefaultsToHighWatermarks()
    {
        // DeepSeek：1M 窗口、最大输出 384K → 输入预算 616K，触发水位被压到 616K。
        var plan = ContextCompactionPolicy.Resolve(new CompactionOptions(Enabled: true));

        Assert.NotNull(plan);
        Assert.Equal(616_000, plan.TriggerTokens);
        Assert.Equal(500_000, plan.TargetTokens);
        Assert.Equal(32, plan.MinimumPreservedGroups);
        Assert.Equal(30, plan.MinimumIntervalMinutes);

        Assert.NotNull(ContextCompactionPolicy.Create(
            new CompactionOptions(Enabled: true),
            new RecordingChatClient()));
    }

    [Fact]
    public void MissingSummarizer_DisablesCompaction()
    {
        Assert.NotNull(ContextCompactionPolicy.Resolve(new CompactionOptions(Enabled: true)));
        Assert.Null(ContextCompactionPolicy.Create(new CompactionOptions(Enabled: true), summarizer: null));
    }

    [Fact]
    public void MiMoOutputLimit_GivesMoreInputBudget()
    {
        // MiMo-V2.6-Flash：1M 窗口、最大输出 128K → 输入预算 872K，700K 触发不再被截。
        var plan = ContextCompactionPolicy.Resolve(new CompactionOptions(
            Enabled: true,
            MaxOutputTokens: 128_000));

        Assert.NotNull(plan);
        Assert.Equal(700_000, plan.TriggerTokens);
        Assert.Equal(500_000, plan.TargetTokens);
    }

    [Fact]
    public void LowThresholds_AreRaisedAboveFloor()
    {
        var plan = ContextCompactionPolicy.Resolve(new CompactionOptions(
            Enabled: true,
            TriggerTokens: 100_000,
            TargetTokens: 100_000));

        Assert.NotNull(plan);
        Assert.Equal(CompactionOptions.FloorTokens + 1, plan.TriggerTokens);
        Assert.Equal(CompactionOptions.FloorTokens, plan.TargetTokens);
    }

    [Fact]
    public void SmallWindow_DisablesCompactionInsteadOfCrossingFloor()
    {
        var plan = ContextCompactionPolicy.Resolve(new CompactionOptions(
            Enabled: true,
            MaxContextWindowTokens: 505_000,
            MaxOutputTokens: 8_192));

        Assert.Null(plan);
    }

    [Fact]
    public void Trigger_IsCappedByInputBudget()
    {
        var plan = ContextCompactionPolicy.Resolve(new CompactionOptions(
            Enabled: true,
            MaxContextWindowTokens: 600_000,
            MaxOutputTokens: 8_000));

        Assert.NotNull(plan);
        Assert.Equal(592_000, plan.TriggerTokens);
        Assert.Equal(500_000, plan.TargetTokens);
    }

    [Fact]
    public void Gate_BlocksBelowFloorAndDuringCooldown()
    {
        var gate = new CompactionGate(600_000, TimeSpan.FromMinutes(30));
        var now = new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

        Assert.False(gate.ShouldCompact(500_000, now));
        Assert.True(gate.ShouldCompact(600_001, now));
        Assert.False(gate.ShouldCompact(600_001, now.AddMinutes(29)));
        Assert.True(gate.ShouldCompact(600_001, now.AddMinutes(30)));
    }
}
