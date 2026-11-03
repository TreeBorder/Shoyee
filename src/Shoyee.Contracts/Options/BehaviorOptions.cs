namespace Shoyee.Contracts.Options;

/// <summary>主动行为。</summary>
/// <param name="Heartbeat">心跳/思念。</param>
/// <param name="Cron">是否启用 cron。</param>
/// <param name="CronJobs">cron 调度参数（可空 = 默认值）。</param>
public sealed record ProactiveOptions(
    HeartbeatOptions Heartbeat,
    bool Cron = true,
    CronOptions? CronJobs = null)
{
    /// <summary>实际使用的 cron 参数。</summary>
    public CronOptions EffectiveCron => CronJobs ?? new CronOptions();
}

/// <summary>cron 调度参数。</summary>
/// <param name="MaxJobs">任务数量上限（用户/智能体都能加，防止无限增长）。</param>
/// <param name="MaxFailureStreak">连续失败多少次后自动停用任务。</param>
/// <param name="ScriptTimeoutSeconds">脚本任务的超时秒数。</param>
public sealed record CronOptions(
    int MaxJobs = 50,
    int MaxFailureStreak = 5,
    int ScriptTimeoutSeconds = 300);

/// <summary>
/// 心跳配置（参数沿用 Harem 已验证的一套：三档闲置 + 思念值驱动的触发概率）。
///
/// 判定顺序：静默时段 → 炽热区（刚聊完，完全不动）→ 温存区（只涨思念）→ 冷宫区（涨思念 + 按概率触发）。
/// 思念值是 0–<see cref="LongingCap"/> 的整数，存在通用状态表 <c>longing</c>。</summary>
/// <param name="Enabled">是否启用心跳。</param>
/// <param name="MinMinutes">调度间隔下限（分钟）：到点跑一次判定，不代表一定发消息。</param>
/// <param name="MaxMinutes">调度间隔上限（分钟）；每次在 [Min, Max] 里随机。</param>
/// <param name="CooldownMinutes">冷宫区阈值：闲置超过它才可能触发问候。</param>
/// <param name="HotZoneMinutes">炽热区阈值：闲置不超过它时，连思念值都不涨。</param>
/// <param name="LongingCap">思念值上限。</param>
/// <param name="NormalIncreaseMin">每次判定时思念值增量下限。</param>
/// <param name="NormalIncreaseMax">每次判定时思念值增量上限（含）。</param>
/// <param name="TriggerPenalty">触发一次后扣掉的思念值。</param>
/// <param name="TriggerThreshold">触发阈值：冷宫区思念值低于它时，再高的概率也不触发。</param>
/// <param name="LevelThresholds">档位阈值（从低到高）；长度决定最高档，默认 [15, 30, 50, 70] 对应 l1–l5。</param>
/// <param name="LevelFluctuationChance">档位波动概率（百分比）：偶尔上下浮动一档，避免每次都是同一个语气。</param>
/// <param name="QuietHoursStart">静默时段起（本地时间，含）；null = 23:00。</param>
/// <param name="QuietHoursEnd">静默时段止（本地时间，不含）；null = 06:30；起止相同 = 不设静默。</param>
public sealed record HeartbeatOptions(
    bool Enabled = false,
    int MinMinutes = 45,
    int MaxMinutes = 75,
    int CooldownMinutes = 30,
    int HotZoneMinutes = 20,
    int LongingCap = 100,
    int NormalIncreaseMin = 1,
    int NormalIncreaseMax = 7,
    int TriggerPenalty = 50,
    int TriggerThreshold = 80,
    IReadOnlyList<int>? LevelThresholds = null,
    int LevelFluctuationChance = 20,
    TimeOnly? QuietHoursStart = null,
    TimeOnly? QuietHoursEnd = null)
{
    /// <summary>默认档位阈值（Harem 原值）。</summary>
    public static IReadOnlyList<int> DefaultLevelThresholds { get; } = [15, 30, 50, 70];

    /// <summary>实际使用的档位阈值（没配就用默认）。</summary>
    public IReadOnlyList<int> EffectiveLevelThresholds
        => LevelThresholds is { Count: > 0 } thresholds ? thresholds : DefaultLevelThresholds;

    /// <summary>实际静默时段起点。</summary>
    public TimeOnly EffectiveQuietStart => QuietHoursStart ?? new TimeOnly(23, 0);

    /// <summary>实际静默时段终点。</summary>
    public TimeOnly EffectiveQuietEnd => QuietHoursEnd ?? new TimeOnly(6, 30);
}
