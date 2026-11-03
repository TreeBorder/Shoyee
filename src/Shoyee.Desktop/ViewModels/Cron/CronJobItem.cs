using Shoyee.Contracts.Dtos;

namespace Shoyee.Desktop.ViewModels.Cron;

/// <summary>定时任务列表项 / 详情头（来自 <c>cron.list</c> / <c>cron.get</c>）。</summary>
public sealed class CronJobItem
{
    /// <summary>从 RPC 的 <see cref="CronJobDto"/> 投影。</summary>
    /// <param name="job">任务定义。</param>
    public CronJobItem(CronJobDto job)
    {
        Id = job.Id;
        Name = string.IsNullOrWhiteSpace(job.Name) ? "未命名任务" : job.Name;
        Schedule = job.Schedule;
        Kind = job.Kind;
        Payload = job.Payload;
        Deliver = job.Deliver;
        Enabled = job.Enabled;
        NextRunAt = job.NextRunAt;
        LastRunAt = job.LastRunAt;
        LastStatus = job.LastStatus;
        LastError = job.LastError;
        FailureStreak = job.FailureStreak;
        CreatedAt = job.CreatedAt;
    }

    /// <summary>任务 id。</summary>
    public string Id { get; }

    /// <summary>名字。</summary>
    public string Name { get; }

    /// <summary>原始调度串。</summary>
    public string Schedule { get; }

    /// <summary>执行方式（prompt / script / message）。</summary>
    public string Kind { get; }

    /// <summary>提示词 / 命令 / 固定文本。</summary>
    public string Payload { get; }

    /// <summary>附加投递。</summary>
    public string Deliver { get; }

    /// <summary>是否启用。</summary>
    public bool Enabled { get; }

    /// <summary>下次触发时间。</summary>
    public DateTimeOffset? NextRunAt { get; }

    /// <summary>最近运行时间。</summary>
    public DateTimeOffset? LastRunAt { get; }

    /// <summary>上次状态。</summary>
    public string? LastStatus { get; }

    /// <summary>上次失败原因。</summary>
    public string? LastError { get; }

    /// <summary>连续失败次数。</summary>
    public int FailureStreak { get; }

    /// <summary>创建时间。</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>调度的人话。</summary>
    public string ScheduleLabel => CronText.Schedule(Schedule);

    /// <summary>执行方式的人话。</summary>
    public string KindLabel => CronText.Kind(Kind);

    /// <summary>投递的人话。</summary>
    public string DeliverLabel => CronText.Deliver(Deliver);

    /// <summary>下次触发（含相对时间）。</summary>
    public string NextRunText => CronText.NextRun(NextRunAt);

    /// <summary>最近运行。</summary>
    public string LastRunText => CronText.Time(LastRunAt);

    /// <summary>上次状态。</summary>
    public string LastStatusLabel => CronText.Status(LastStatus);

    /// <summary>创建时间。</summary>
    public string CreatedText => CronText.Time(CreatedAt);

    /// <summary>状态徽标文案。</summary>
    public string EnabledLabel => Enabled ? "启用" : "停用";

    /// <summary>是否停用（给徽标配色）。</summary>
    public bool IsDisabled => !Enabled;

    /// <summary>是否上次失败。</summary>
    public bool IsFailed => LastStatus == "failed";

    /// <summary>是否有失败原因。</summary>
    public bool HasLastError => !string.IsNullOrWhiteSpace(LastError);

    /// <summary>连续失败说明。</summary>
    public string FailureLabel => FailureStreak > 0 ? $"连续失败 {FailureStreak} 次" : "";

    /// <summary>列表里的副标题：调度 + 下次触发。</summary>
    public string ListSubtitle => $"{ScheduleLabel} · 下次 {NextRunText}";
}
