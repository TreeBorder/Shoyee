using Shoyee.Contracts.Domain;

namespace Shoyee.Storage.Abstractions;

/// <summary>一个 cron 任务。</summary>
/// <param name="Id">任务 id。</param>
/// <param name="Name">名字。</param>
/// <param name="Schedule">调度串：<c>once:ISO</c> / <c>every:30m</c> / Quartz cron 表达式。</param>
/// <param name="Kind">执行方式：prompt（跑 LLM）/ script（跑命令）/ message（固定文本）。</param>
/// <param name="Payload">提示词、命令或固定文本。</param>
/// <param name="Deliver">附加投递：<c>local</c> / <c>push</c>，可用逗号组合。</param>
/// <param name="Enabled">是否启用。</param>
/// <param name="LastRunAt">最近运行时间。</param>
/// <param name="CreatedAt">创建时间。</param>
/// <param name="Mode">所属模式。</param>
/// <param name="LaneId">RP 任务的投递 Lane（工作模式为 null）。</param>
/// <param name="SessionId">工作模式 prompt 任务的独立执行会话（首次运行时创建）。</param>
/// <param name="NextRunAt">下次触发时间（展示与排障用；Quartz 才是实际调度者）。</param>
/// <param name="LastStatus">上次状态（succeeded / failed）。</param>
/// <param name="LastError">上次失败原因。</param>
/// <param name="FailureStreak">连续失败次数（达到阈值自动停用）。</param>
public sealed record CronJobRecord(
    string Id,
    string Name,
    string Schedule,
    string Kind,
    string Payload,
    string Deliver,
    bool Enabled,
    DateTimeOffset? LastRunAt,
    DateTimeOffset CreatedAt,
    Mode Mode = Mode.Work,
    string? LaneId = null,
    string? SessionId = null,
    DateTimeOffset? NextRunAt = null,
    string? LastStatus = null,
    string? LastError = null,
    int FailureStreak = 0);
