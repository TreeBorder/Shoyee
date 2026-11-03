using Microsoft.Agents.AI.Compaction;
using Microsoft.Extensions.AI;
using Shoyee.Contracts.Options;

namespace Shoyee.Agent.Composition;

/// <summary>
/// 一次压缩的生效水位。单独抽出来便于测试与日志展示。
/// </summary>
/// <param name="TriggerTokens">达到即压缩的水位。</param>
/// <param name="TargetTokens">压到低于它即停的水位。</param>
/// <param name="MinimumPreservedGroups">最近至少保留的消息组数。</param>
/// <param name="MinimumIntervalMinutes">两次压缩之间的最短间隔（分钟）。</param>
public sealed record ContextCompactionPlan(
    int TriggerTokens,
    int TargetTokens,
    int MinimumPreservedGroups,
    int MinimumIntervalMinutes);

/// <summary>
/// 压缩闸门：同时满足「超过触发水位」和「距上次触发已过最短间隔」才放行。
///
/// 框架自带的 <c>TokensExceed</c> 只保证「涨回去再压」，这里再加一道时间冷却：
/// 万一一次摘要没压到目标水位（最近 32 组本身就很大），也不会在下一轮继续压。
/// </summary>
public sealed class CompactionGate
{
    private readonly int _triggerTokens;
    private readonly TimeSpan _cooldown;
    private DateTimeOffset? _lastTriggered;

    /// <summary>创建闸门。</summary>
    /// <param name="triggerTokens">触发水位。</param>
    /// <param name="cooldown">最短间隔。</param>
    public CompactionGate(int triggerTokens, TimeSpan cooldown)
    {
        _triggerTokens = triggerTokens;
        _cooldown = cooldown;
    }

    /// <summary>这次是否放行；放行时会记下时间。</summary>
    /// <param name="tokens">当前请求的估算 token 数。</param>
    /// <param name="now">当前时间。</param>
    public bool ShouldCompact(int tokens, DateTimeOffset now)
    {
        if (tokens <= _triggerTokens)
        {
            return false;
        }

        if (_lastTriggered is { } last && now - last < _cooldown)
        {
            return false;
        }

        _lastTriggered = now;
        return true;
    }
}

/// <summary>
/// 把工作区配置里的 <see cref="CompactionOptions"/> 换成框架的压缩策略。
///
/// 只给工作模式的 <see cref="WorkAgentFactory"/> 用；RP 的摘要走
/// <c>RoleplaySummarizer</c>，不读这一段。配置来自手工编辑的 config.json，
/// 这里做一次范围收敛，避免非法值让 daemon 启动时抛异常。
///
/// 四条硬规则（见 <see cref="CompactionOptions"/>）：
/// 1. **500K 以下不压**：触发水位与目标水位都不得低于 500K；
/// 2. **不频繁压**：触发水位明显高于目标水位，且两次压缩至少隔 30 分钟；
/// 3. **不直接丢历史**：只做「旧消息 → 摘要」，最近 N 组原样保留；小模型不可用就整个关掉，
///    不退化成截断。压掉的只是**下一次请求**里的旧消息，落库的会话历史不动；
/// 4. **窗口/输出按模型规格**：DeepSeek 1M / 384K、MiMo-V2.6-Flash 1M / 128K。
/// </summary>
public static class ContextCompactionPolicy
{
    /// <summary>计算生效水位；未启用或窗口太小（无法在不碰 500K 的前提下压缩）返回 null。</summary>
    /// <param name="options">压缩配置；null = 默认参数。</param>
    public static ContextCompactionPlan? Resolve(CompactionOptions? options)
    {
        var effective = options ?? CompactionOptions.Default;
        if (!effective.Enabled)
        {
            return null;
        }

        // 窗口至少 1k；输出上限必须小于窗口（框架构造器明确拒绝 >= 窗口）。
        var window = Math.Max(1024, effective.MaxContextWindowTokens);
        var output = Math.Clamp(effective.MaxOutputTokens, 0, window - 1);
        var inputBudget = window - output;

        // 窗口小到连 500K 都装不下：宁可不压，也不破「500K 以下不压」。
        if (inputBudget <= CompactionOptions.FloorTokens)
        {
            return null;
        }

        var trigger = Math.Clamp(
            effective.TriggerTokens,
            CompactionOptions.FloorTokens + 1,
            inputBudget);
        var target = Math.Clamp(
            effective.TargetTokens,
            CompactionOptions.FloorTokens,
            trigger - 1);
        var preserved = Math.Max(1, effective.MinimumPreservedGroups);
        var interval = Math.Max(0, effective.MinimumIntervalMinutes);

        return new ContextCompactionPlan(trigger, target, preserved, interval);
    }

    /// <summary>创建入循环压缩策略；未启用、窗口太小或没有小模型时返回 null。</summary>
    /// <param name="options">压缩配置；null = 默认参数。</param>
    /// <param name="summarizer">生成摘要的模型（建议用小模型）；null = 不做压缩。</param>
    public static CompactionStrategy? Create(CompactionOptions? options, IChatClient? summarizer)
    {
        if (Resolve(options) is not { } plan || summarizer is null)
        {
            return null;
        }

        // 触发器自带水位 + 冷却；目标水位保证压完有回涨空间。
        var gate = new CompactionGate(plan.TriggerTokens, TimeSpan.FromMinutes(plan.MinimumIntervalMinutes));
        CompactionTrigger trigger = index => gate.ShouldCompact(index.IncludedTokenCount, DateTimeOffset.UtcNow);

        return new SummarizationCompactionStrategy(
            summarizer,
            trigger,
            plan.MinimumPreservedGroups,
            null,
            CompactionTriggers.TokensBelow(plan.TargetTokens));
    }
}
