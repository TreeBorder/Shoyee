using Shoyee.Contracts.Options;

namespace Shoyee.Proactive.Heartbeat;

/// <summary>一次判定的分区结果。</summary>
public enum HeartbeatZone
{
    /// <summary>心跳没启用。</summary>
    Disabled,

    /// <summary>静默时段：不涨思念、不发消息。</summary>
    QuietHours,

    /// <summary>炽热区（刚聊完）：完全不动。</summary>
    Hot,

    /// <summary>温存区：涨思念，不发消息。</summary>
    Warm,

    /// <summary>冷宫区：涨思念，并按概率决定是否触发。</summary>
    Cold,
}

/// <summary>一次心跳判定的结果。</summary>
/// <param name="Zone">所在分区。</param>
/// <param name="Longing">更新后的思念值（炽热/静默/停用时原样返回）。</param>
/// <param name="Triggered">这一轮是否要主动发消息。</param>
/// <param name="Level">语气档位（0 起；<c>Level + 1</c> 对应提示词文件的 l1–l5）。</param>
/// <param name="PromptType">提示词类别（text / story）；不触发时为 null。</param>
public sealed record HeartbeatDecision(
    HeartbeatZone Zone,
    int Longing,
    bool Triggered,
    int Level = 0,
    string? PromptType = null);

/// <summary>
/// 心跳判定（纯函数，随机性由调用方以 <c>next</c> 注入，测试可完全控制）。
///
/// 规则沿用 Harem：
/// - 闲置 &lt; <see cref="HeartbeatOptions.HotZoneMinutes"/>：炽热区，什么都不动；
/// - 闲置 &lt; <see cref="HeartbeatOptions.CooldownMinutes"/>：温存区，只涨思念；
/// - 否则冷宫区：涨思念后若仍低于 <see cref="HeartbeatOptions.TriggerThreshold"/> 直接不触发
///   （Harem 特殊模式的做法：阈值抬高，频率自然降下来），否则按
///   <c>思念值 / LongingCap</c> 的概率触发，触发后扣 <see cref="HeartbeatOptions.TriggerPenalty"/>，
///   并按触发前的思念值算语气档位。
/// </summary>
public static class HeartbeatPolicy
{
    /// <summary>是否处于静默时段（支持跨零点；起止相同 = 不静默）。</summary>
    /// <param name="localTime">本地时间。</param>
    /// <param name="options">心跳配置。</param>
    public static bool IsQuietHours(TimeOnly localTime, HeartbeatOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var start = options.EffectiveQuietStart;
        var end = options.EffectiveQuietEnd;
        if (start == end)
        {
            return false;
        }

        return start < end
            ? localTime >= start && localTime < end
            : localTime >= start || localTime < end;
    }

    /// <summary>判定这一次心跳。</summary>
    /// <param name="localTime">当前本地时间（判静默时段）。</param>
    /// <param name="idleMinutes">距上次互动的分钟数（跨模式跨渠道）。</param>
    /// <param name="longing">当前思念值。</param>
    /// <param name="options">心跳配置。</param>
    /// <param name="next">取 <c>[0, max)</c> 的随机整数；测试可注入固定序列。</param>
    public static HeartbeatDecision Decide(
        TimeOnly localTime,
        int idleMinutes,
        int longing,
        HeartbeatOptions options,
        Func<int, int> next)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(next);

        if (!options.Enabled)
        {
            return new HeartbeatDecision(HeartbeatZone.Disabled, longing, false);
        }

        if (IsQuietHours(localTime, options))
        {
            return new HeartbeatDecision(HeartbeatZone.QuietHours, longing, false);
        }

        var hot = Math.Max(0, options.HotZoneMinutes);
        var cold = Math.Max(hot, options.CooldownMinutes);
        if (idleMinutes < hot)
        {
            return new HeartbeatDecision(HeartbeatZone.Hot, longing, false);
        }

        var cap = Math.Max(1, options.LongingCap);
        var increased = Math.Clamp(
            longing + NextRange(next, options.NormalIncreaseMin, options.NormalIncreaseMax),
            0,
            cap);

        if (idleMinutes < cold)
        {
            return new HeartbeatDecision(HeartbeatZone.Warm, increased, false);
        }

        // 阈值门槛：思念还没攒够时不掷骰子。这是「特殊模式」被简化后保留的那部分。
        if (increased < Math.Max(0, options.TriggerThreshold))
        {
            return new HeartbeatDecision(HeartbeatZone.Cold, increased, false);
        }

        // 触发概率 = 思念值 / 上限（思念 100 = 必触发）
        var roll = next(cap) + 1;
        if (roll > increased)
        {
            return new HeartbeatDecision(HeartbeatZone.Cold, increased, false);
        }

        var level = LevelOf(increased, options, next);
        var afterPenalty = Math.Max(0, increased - Math.Max(0, options.TriggerPenalty));
        var promptType = next(2) == 0 ? "text" : "story";
        return new HeartbeatDecision(HeartbeatZone.Cold, afterPenalty, true, level, promptType);
    }

    private static int NextRange(Func<int, int> next, int min, int max)
    {
        if (max < min)
        {
            (min, max) = (max, min);
        }

        return min + next(Math.Max(1, max - min + 1));
    }

    private static int LevelOf(int longing, HeartbeatOptions options, Func<int, int> next)
    {
        var thresholds = options.EffectiveLevelThresholds;
        var maxLevel = thresholds.Count;
        var level = 0;
        for (var i = thresholds.Count - 1; i >= 0; i--)
        {
            if (longing >= thresholds[i])
            {
                level = i + 1;
                break;
            }
        }

        if (maxLevel > 0 && next(100) < options.LevelFluctuationChance)
        {
            var delta = next(2) == 0 ? 1 : -1;
            level = Math.Clamp(level + delta, 0, maxLevel);
        }

        return level;
    }
}
