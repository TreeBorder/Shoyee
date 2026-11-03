using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shoyee.Contracts.Abstractions;

namespace Shoyee.Proactive.Cron;

/// <summary>
/// 启动时把库里的任务全部排进 Quartz。
///
/// Quartz 用内存存储：进程重启后触发器本来就是空的，所以每次启动都要重新排一遍；
/// 同一次触发会不会重复跑由 <c>cron_executions</c> 的唯一约束兜底，不靠 Quartz 的持久化。
/// </summary>
public sealed class CronBootstrapService : IHostedService
{
    private readonly ICronScheduler _scheduler;
    private readonly ILogger<CronBootstrapService> _logger;

    /// <summary>创建启动器。</summary>
    /// <param name="scheduler">cron 调度面。</param>
    /// <param name="logger">日志。</param>
    public CronBootstrapService(ICronScheduler scheduler, ILogger<CronBootstrapService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        _scheduler = scheduler;
        _logger = logger ?? NullLogger<CronBootstrapService>.Instance;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _scheduler.RefreshAsync(jobId: null, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 一个坏任务不能拦住 daemon 启动；后面工具改任务时还会再刷新
            _logger.LogError(ex, "cron 任务启动排程失败");
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
