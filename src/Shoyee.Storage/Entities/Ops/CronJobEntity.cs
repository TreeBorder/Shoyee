using Shoyee.Contracts.Domain;

namespace Shoyee.Storage.Entities.Ops;

/// <summary>cron 任务。</summary>
public sealed class CronJobEntity
{
    /// <summary>任务 id。</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>名字。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>调度串（once: / every: / Quartz cron）。</summary>
    public string Schedule { get; set; } = string.Empty;

    /// <summary>种类（prompt / script / message）。</summary>
    public string Kind { get; set; } = "prompt";

    /// <summary>提示词、命令或固定文本。</summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>附加投递（local / push，可逗号组合）。</summary>
    public string Deliver { get; set; } = "local";

    /// <summary>所属模式。</summary>
    public Mode Mode { get; set; } = Mode.Work;

    /// <summary>RP 任务的投递 Lane。</summary>
    public string? LaneId { get; set; }

    /// <summary>工作模式 prompt 任务的独立执行会话。</summary>
    public string? SessionId { get; set; }

    /// <summary>是否启用。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>最近运行时间（UTC）。</summary>
    public DateTimeOffset? LastRunAt { get; set; }

    /// <summary>下次触发时间（UTC）。</summary>
    public DateTimeOffset? NextRunAt { get; set; }

    /// <summary>上次状态（succeeded / failed）。</summary>
    public string? LastStatus { get; set; }

    /// <summary>上次失败原因。</summary>
    public string? LastError { get; set; }

    /// <summary>连续失败次数。</summary>
    public int FailureStreak { get; set; }

    /// <summary>创建时间（UTC）。</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
