namespace Shoyee.Storage.Abstractions;

/// <summary>一条 cron 执行记录（任务页的「单次执行结果」）。</summary>
/// <param name="JobId">任务 id。</param>
/// <param name="ScheduledFor">本次触发计划时间（UTC，去重键的一半）。</param>
/// <param name="StartedAt">开始时间。</param>
/// <param name="FinishedAt">结束时间（还在跑时为 null）。</param>
/// <param name="State">running / succeeded / failed。</param>
/// <param name="Result">产出正文或错误说明。</param>
public sealed record CronExecutionRecord(
    string JobId,
    DateTimeOffset ScheduledFor,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    string State,
    string? Result);
