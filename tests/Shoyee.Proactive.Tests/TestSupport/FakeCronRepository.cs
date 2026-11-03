using Shoyee.Storage.Abstractions;

namespace Shoyee.Proactive.Tests.TestSupport;

/// <summary>内存 cron 仓库（任务 + 执行台账 + 触发认领）。</summary>
internal sealed class FakeCronRepository : ICronRepository
{
    private readonly Dictionary<string, CronJobRecord> _jobs = new(StringComparer.Ordinal);
    private readonly List<CronExecutionRecord> _executions = [];
    private readonly HashSet<(string JobId, DateTimeOffset ScheduledFor)> _claims = [];

    public IReadOnlyDictionary<string, CronJobRecord> Rows => _jobs;

    public Task<IReadOnlyList<CronJobRecord>> ListAsync(bool enabledOnly = true, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<CronJobRecord>>(
            _jobs.Values.Where(job => !enabledOnly || job.Enabled).OrderBy(job => job.Id).ToList());

    public Task<CronJobRecord?> GetAsync(string jobId, CancellationToken cancellationToken = default)
        => Task.FromResult(_jobs.TryGetValue(jobId, out var job) ? job : null);

    public Task<CronJobRecord> UpsertAsync(CronJobRecord job, CancellationToken cancellationToken = default)
    {
        _jobs[job.Id] = job;
        return Task.FromResult(job);
    }

    public Task<bool> DeleteAsync(string jobId, CancellationToken cancellationToken = default)
        => Task.FromResult(_jobs.Remove(jobId));

    public Task<bool> SetEnabledAsync(string jobId, bool enabled, CancellationToken cancellationToken = default)
    {
        if (!_jobs.TryGetValue(jobId, out var job))
        {
            return Task.FromResult(false);
        }

        _jobs[jobId] = job with { Enabled = enabled, NextRunAt = enabled ? job.NextRunAt : null };
        return Task.FromResult(true);
    }

    public Task SetNextRunAsync(string jobId, DateTimeOffset? nextRunAt, CancellationToken cancellationToken = default)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            _jobs[jobId] = job with { NextRunAt = nextRunAt };
        }

        return Task.CompletedTask;
    }

    public Task SetStateAsync(string jobId, string? lastStatus, string? lastError, int failureStreak, CancellationToken cancellationToken = default)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            _jobs[jobId] = job with { LastStatus = lastStatus, LastError = lastError, FailureStreak = failureStreak };
        }

        return Task.CompletedTask;
    }

    public Task<bool> TryClaimAsync(string jobId, DateTimeOffset scheduledFor, CancellationToken cancellationToken = default)
    {
        if (!_claims.Add((jobId, scheduledFor)))
        {
            return Task.FromResult(false);
        }

        _executions.Add(new CronExecutionRecord(jobId, scheduledFor, DateTimeOffset.UtcNow, null, "running", null));
        return Task.FromResult(true);
    }

    public Task CompleteAsync(string jobId, DateTimeOffset scheduledFor, string state, string? result, CancellationToken cancellationToken = default)
    {
        var index = _executions.FindIndex(e => e.JobId == jobId && e.ScheduledFor == scheduledFor);
        if (index >= 0)
        {
            _executions[index] = _executions[index] with { State = state, Result = result, FinishedAt = DateTimeOffset.UtcNow };
        }

        if (_jobs.TryGetValue(jobId, out var job))
        {
            _jobs[jobId] = job with { LastRunAt = DateTimeOffset.UtcNow };
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CronExecutionRecord>> ListExecutionsAsync(string jobId, int limit = 10, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<CronExecutionRecord>>(
            _executions.Where(e => e.JobId == jobId)
                .OrderByDescending(e => e.ScheduledFor)
                .Take(limit)
                .ToList());
}
