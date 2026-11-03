using Quartz;

namespace Shoyee.Proactive.Cron;

/// <summary>
/// Quartz 触发器落到这里：拿 jobId 与计划触发时间，交给 <see cref="CronRunner"/>。
/// <see cref="DisallowConcurrentExecutionAttribute"/>：同一任务上一次没跑完就不再开新的。
/// </summary>
[DisallowConcurrentExecution]
public sealed class CronJobExecution : IJob
{
    private readonly CronRunner _runner;

    /// <summary>创建 job。</summary>
    /// <param name="runner">执行器（DI 每次触发时构建）。</param>
    public CronJobExecution(CronRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
    }

    /// <inheritdoc />
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        var jobId = context.MergedJobDataMap.GetString("jobId");
        if (string.IsNullOrWhiteSpace(jobId))
        {
            return;
        }

        await _runner
            .ExecuteAsync(
                jobId,
                context.ScheduledFireTimeUtc ?? DateTimeOffset.UtcNow,
                context.NextFireTimeUtc,
                cancellationToken)
            .ConfigureAwait(false);
    }
}
