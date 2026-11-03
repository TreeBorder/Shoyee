using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Quartz;
using Shoyee.Contracts.Abstractions;
using Shoyee.Contracts.Options;
using Shoyee.Storage.Abstractions;

namespace Shoyee.Proactive.Cron;

/// <summary>
/// Quartz 调度面：把 <c>cron_jobs</c> 的定义翻译成 Quartz 触发器。
///
/// 调度状态只在 Quartz 内存里；库里的 <c>next_run_at</c> 只用于展示和排障。
/// 每次触发仍靠 <c>cron_executions(job_id, scheduled_for)</c> 唯一约束去重，
/// 所以进程重启、Quartz 重排都不会把同一次触发跑两遍。
/// </summary>
public sealed class CronJobScheduler : ICronScheduler
{
    /// <summary>Quartz 里 cron 任务的 group。</summary>
    public const string Group = "cron";

    private readonly ISchedulerFactory _schedulerFactory;
    private readonly ICronRepository _jobs;
    private readonly ProactiveOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<CronJobScheduler> _logger;

    /// <summary>创建调度面。</summary>
    /// <param name="schedulerFactory">Quartz 调度器工厂。</param>
    /// <param name="jobs">cron 仓库。</param>
    /// <param name="config">工作区配置（取 <c>proactive</c>）。</param>
    /// <param name="clock">时间源（测试可注入）。</param>
    /// <param name="logger">日志。</param>
    public CronJobScheduler(
        ISchedulerFactory schedulerFactory,
        ICronRepository jobs,
        WorkspaceConfig config,
        TimeProvider? clock = null,
        ILogger<CronJobScheduler>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(schedulerFactory);
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(config);
        _schedulerFactory = schedulerFactory;
        _jobs = jobs;
        _options = config.Proactive;
        _clock = clock ?? TimeProvider.System;
        _logger = logger ?? NullLogger<CronJobScheduler>.Instance;
    }

    /// <inheritdoc />
    public string? Validate(string schedule) => CronScheduleParser.Validate(schedule);

    /// <inheritdoc />
    public async Task RefreshAsync(string? jobId = null, CancellationToken cancellationToken = default)
    {
        var scheduler = await _schedulerFactory.GetScheduler(cancellationToken).ConfigureAwait(false);
        if (jobId is not null)
        {
            await ApplyAsync(scheduler, jobId, cancellationToken).ConfigureAwait(false);
            return;
        }

        var jobs = await _jobs.ListAsync(enabledOnly: false, cancellationToken).ConfigureAwait(false);
        foreach (var job in jobs)
        {
            await ApplyAsync(scheduler, job.Id, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ApplyAsync(IScheduler scheduler, string jobId, CancellationToken cancellationToken)
    {
        var job = await _jobs.GetAsync(jobId, cancellationToken).ConfigureAwait(false);
        var jobKey = new JobKey(jobId, Group);
        if (job is null || !job.Enabled || !_options.Cron)
        {
            await scheduler.DeleteJob(jobKey, cancellationToken).ConfigureAwait(false);
            await _jobs.SetNextRunAsync(jobId, null, cancellationToken).ConfigureAwait(false);
            return;
        }

        CronScheduleSpec spec;
        try
        {
            spec = CronScheduleParser.Parse(job.Schedule);
        }
        catch (FormatException ex)
        {
            _logger.LogError(ex, "cron 任务调度非法，已停用：{JobId} {Name}", job.Id, job.Name);
            await _jobs.SetEnabledAsync(job.Id, false, cancellationToken).ConfigureAwait(false);
            await _jobs.SetStateAsync(job.Id, "failed", ex.Message, job.FailureStreak, cancellationToken)
                .ConfigureAwait(false);
            await _jobs.SetNextRunAsync(job.Id, null, cancellationToken).ConfigureAwait(false);
            await scheduler.DeleteJob(jobKey, cancellationToken).ConfigureAwait(false);
            return;
        }

        var now = _clock.GetUtcNow();
        if (spec.Kind == CronScheduleKind.Once && spec.RunAt <= now)
        {
            _logger.LogWarning("cron 一次性任务已过点，不再调度：{JobId} {Name}（{RunAt:O}）", job.Id, job.Name, spec.RunAt);
            await scheduler.DeleteJob(jobKey, cancellationToken).ConfigureAwait(false);
            await _jobs.SetNextRunAsync(job.Id, null, cancellationToken).ConfigureAwait(false);
            return;
        }

        var detail = JobBuilder.Create<CronJobExecution>()
            .WithIdentity(jobKey)
            .UsingJobData("jobId", job.Id)
            .Build();

        var trigger = TriggerBuilder.Create()
            .WithIdentity(new TriggerKey(job.Id, Group))
            .StartAt(spec.Kind switch
            {
                CronScheduleKind.Once => spec.RunAt!.Value,
                // 固定间隔从「现在 + 间隔」开始，否则启动瞬间会先补跑一次
                CronScheduleKind.Interval => now + spec.Interval!.Value,
                _ => now,
            })
            .WithSchedule(BuildSchedule(spec))
            .Build();

        var next = await scheduler
            .ScheduleJob(detail, trigger, ScheduleJobOptions.Replacing, cancellationToken)
            .ConfigureAwait(false);

        await _jobs.SetNextRunAsync(job.Id, next, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("cron 已排程：{JobId} {Name}（{Schedule}，下次 {Next:O}）", job.Id, job.Name, job.Schedule, next);
    }

    private static IScheduleBuilder BuildSchedule(CronScheduleSpec spec)
        => spec.Kind switch
        {
            CronScheduleKind.Once => SimpleScheduleBuilder.Create().WithRepeatCount(0),
            CronScheduleKind.Interval => SimpleScheduleBuilder.Create()
                .WithInterval(spec.Interval!.Value)
                .RepeatForever(),
            CronScheduleKind.Cron => CronScheduleBuilder.Create(spec.Cron!),
            _ => throw new ArgumentOutOfRangeException(nameof(spec)),
        };
}
