namespace Shoyee.Storage.Entities.Ops;

/// <summary>cron 执行台账；<c>(JobId, ScheduledFor)</c> 唯一 = 去重锁。</summary>
public sealed class CronExecutionEntity
{
    /// <summary>自增 id。</summary>
    public long Id { get; set; }

    /// <summary>任务 id。</summary>
    public string JobId { get; set; } = string.Empty;

    /// <summary>本次触发的计划时间（UTC）。</summary>
    public DateTimeOffset ScheduledFor { get; set; }

    /// <summary>开始时间（UTC）。</summary>
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>结束时间（UTC）。</summary>
    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>状态（running / succeeded / failed）。</summary>
    public string State { get; set; } = "running";

    /// <summary>结果或错误。</summary>
    public string? Result { get; set; }
}
