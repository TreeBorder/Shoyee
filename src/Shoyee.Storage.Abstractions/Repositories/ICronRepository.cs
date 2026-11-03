namespace Shoyee.Storage.Abstractions;

/// <summary>cron 任务与执行台账。</summary>
public interface ICronRepository
{
    /// <summary>列出任务（默认只取启用的）。</summary>
    Task<IReadOnlyList<CronJobRecord>> ListAsync(bool enabledOnly = true, CancellationToken cancellationToken = default);

    /// <summary>按 id 取任务；不存在返回 null。</summary>
    Task<CronJobRecord?> GetAsync(string jobId, CancellationToken cancellationToken = default);

    /// <summary>新增或覆盖任务定义（运行状态列由调度器单独更新）。</summary>
    Task<CronJobRecord> UpsertAsync(CronJobRecord job, CancellationToken cancellationToken = default);

    /// <summary>删除任务定义；执行台账一并删除。</summary>
    Task<bool> DeleteAsync(string jobId, CancellationToken cancellationToken = default);

    /// <summary>启停任务。</summary>
    Task<bool> SetEnabledAsync(string jobId, bool enabled, CancellationToken cancellationToken = default);

    /// <summary>更新下次触发时间（展示用；null = 不再触发）。</summary>
    Task SetNextRunAsync(string jobId, DateTimeOffset? nextRunAt, CancellationToken cancellationToken = default);

    /// <summary>更新执行状态与连续失败计数。</summary>
    Task SetStateAsync(
        string jobId,
        string? lastStatus,
        string? lastError,
        int failureStreak,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 认领一次触发：靠 <c>(job_id, scheduled_for)</c> 唯一约束当去重锁。
    /// 返回 false = 该触发已经跑过。
    /// </summary>
    Task<bool> TryClaimAsync(string jobId, DateTimeOffset scheduledFor, CancellationToken cancellationToken = default);

    /// <summary>结束一次执行并更新任务的最近运行时间。</summary>
    Task CompleteAsync(
        string jobId,
        DateTimeOffset scheduledFor,
        string state,
        string? result,
        CancellationToken cancellationToken = default);

    /// <summary>最近若干条执行记录（按计划时间倒序）。</summary>
    Task<IReadOnlyList<CronExecutionRecord>> ListExecutionsAsync(
        string jobId,
        int limit = 10,
        CancellationToken cancellationToken = default);
}
