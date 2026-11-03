using Shoyee.Storage.Abstractions;

namespace Shoyee.Handlers.Tests.TestSupport;

/// <summary>内存定时任务仓库（只测列表）。</summary>
internal sealed class FakeCronRepository : ICronRepository
{
    public List<CronJobRecord> Jobs { get; } = [];

    public bool? LastEnabledOnly { get; private set; }

    public Task<IReadOnlyList<CronJobRecord>> ListAsync(
        bool enabledOnly = true,
        CancellationToken cancellationToken = default)
    {
        LastEnabledOnly = enabledOnly;
        return Task.FromResult<IReadOnlyList<CronJobRecord>>(
            enabledOnly ? Jobs.Where(static job => job.Enabled).ToList() : Jobs);
    }

    public Task<CronJobRecord?> GetAsync(string jobId, CancellationToken cancellationToken = default)
        => Task.FromResult(Jobs.FirstOrDefault(job => job.Id == jobId));

    public Task<CronJobRecord> UpsertAsync(CronJobRecord job, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<bool> DeleteAsync(string jobId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<bool> SetEnabledAsync(string jobId, bool enabled, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task SetNextRunAsync(string jobId, DateTimeOffset? nextRunAt, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task SetStateAsync(
        string jobId,
        string? lastStatus,
        string? lastError,
        int failureStreak,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<bool> TryClaimAsync(string jobId, DateTimeOffset scheduledFor, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task CompleteAsync(
        string jobId,
        DateTimeOffset scheduledFor,
        string state,
        string? result,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<IReadOnlyList<CronExecutionRecord>> ListExecutionsAsync(
        string jobId,
        int limit = 10,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
}
