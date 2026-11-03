using Shoyee.Contracts.Dtos;

namespace Shoyee.Desktop.ViewModels.Cron;

/// <summary>一条 cron 执行记录。</summary>
public sealed class CronExecutionItem
{
    /// <summary>从 RPC 的 <see cref="CronExecutionDto"/> 投影。</summary>
    /// <param name="execution">执行记录。</param>
    public CronExecutionItem(CronExecutionDto execution)
    {
        ScheduledFor = execution.ScheduledFor;
        StartedAt = execution.StartedAt;
        FinishedAt = execution.FinishedAt;
        State = execution.State;
        Result = execution.Result;
    }

    /// <summary>计划触发时间。</summary>
    public DateTimeOffset ScheduledFor { get; }

    /// <summary>开始时间。</summary>
    public DateTimeOffset? StartedAt { get; }

    /// <summary>结束时间。</summary>
    public DateTimeOffset? FinishedAt { get; }

    /// <summary>running / succeeded / failed。</summary>
    public string State { get; }

    /// <summary>产出正文或错误说明。</summary>
    public string? Result { get; }

    /// <summary>计划时间（本地）。</summary>
    public string ScheduledText => CronText.Time(ScheduledFor);

    /// <summary>耗时。</summary>
    public string DurationText => CronText.Duration(StartedAt, FinishedAt);

    /// <summary>状态文案。</summary>
    public string StateLabel => CronText.State(State);

    /// <summary>是否失败。</summary>
    public bool IsFailed => State == "failed";

    /// <summary>是否还在跑。</summary>
    public bool IsRunning => State == "running";

    /// <summary>结果正文；没有就给占位。</summary>
    public string ResultText => string.IsNullOrWhiteSpace(Result) ? "（没有产出）" : Result;

    /// <summary>是否有结果。</summary>
    public bool HasResult => !string.IsNullOrWhiteSpace(Result);

    /// <summary>状态 + 耗时。</summary>
    public string MetaText => $"{StateLabel} · {DurationText}";
}
