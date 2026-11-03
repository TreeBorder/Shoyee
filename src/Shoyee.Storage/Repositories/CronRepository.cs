using Microsoft.EntityFrameworkCore;
using Shoyee.Storage.Abstractions;
using Shoyee.Storage.Entities.Ops;

namespace Shoyee.Storage.Repositories;

/// <summary>cron 任务与执行台账仓库（纯 EF）。</summary>
public sealed class CronRepository : ICronRepository
{
    private readonly IDbContextFactory<ShoyeeDbContext> _contextFactory;

    /// <summary>创建仓库。</summary>
    /// <param name="contextFactory">上下文工厂。</param>
    public CronRepository(IDbContextFactory<ShoyeeDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CronJobRecord>> ListAsync(bool enabledOnly = true, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var query = context.CronJobs.AsNoTracking();
        if (enabledOnly)
        {
            query = query.Where(x => x.Enabled);
        }

        var rows = await query.OrderBy(x => x.Id).ToListAsync(cancellationToken);
        return rows.Select(ToRecord).ToList();
    }

    /// <inheritdoc />
    public async Task<CronJobRecord?> GetAsync(string jobId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await context.CronJobs.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == jobId, cancellationToken);
        return row is null ? null : ToRecord(row);
    }

    /// <inheritdoc />
    public async Task<CronJobRecord> UpsertAsync(CronJobRecord job, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(job.Id);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await context.CronJobs.FirstOrDefaultAsync(x => x.Id == job.Id, cancellationToken);
        if (row is null)
        {
            row = new CronJobEntity { Id = job.Id, CreatedAt = job.CreatedAt == default ? DateTimeOffset.UtcNow : job.CreatedAt };
            context.CronJobs.Add(row);
        }

        row.Name = job.Name;
        row.Schedule = job.Schedule;
        row.Kind = job.Kind;
        row.Payload = job.Payload;
        row.Deliver = job.Deliver;
        row.Mode = job.Mode;
        row.LaneId = job.LaneId;
        row.SessionId = job.SessionId;
        row.Enabled = job.Enabled;
        row.LastRunAt = job.LastRunAt;
        row.NextRunAt = job.NextRunAt;
        row.LastStatus = job.LastStatus;
        row.LastError = job.LastError;
        row.FailureStreak = job.FailureStreak;

        await context.SaveChangesAsync(cancellationToken);
        return ToRecord(row);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string jobId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await context.CronExecutions
            .Where(x => x.JobId == jobId)
            .ExecuteDeleteAsync(cancellationToken);
        var deleted = await context.CronJobs
            .Where(x => x.Id == jobId)
            .ExecuteDeleteAsync(cancellationToken);
        return deleted > 0;
    }

    /// <inheritdoc />
    public async Task<bool> SetEnabledAsync(string jobId, bool enabled, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var updated = await context.CronJobs
            .Where(x => x.Id == jobId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(x => x.Enabled, enabled),
                cancellationToken);
        if (updated > 0 && !enabled)
        {
            await context.CronJobs
                .Where(x => x.Id == jobId)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(x => x.NextRunAt, (DateTimeOffset?)null),
                    cancellationToken);
        }

        return updated > 0;
    }

    /// <inheritdoc />
    public async Task SetNextRunAsync(string jobId, DateTimeOffset? nextRunAt, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await context.CronJobs
            .Where(x => x.Id == jobId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(x => x.NextRunAt, nextRunAt),
                cancellationToken);
    }

    /// <inheritdoc />
    public async Task SetStateAsync(
        string jobId,
        string? lastStatus,
        string? lastError,
        int failureStreak,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await context.CronJobs
            .Where(x => x.Id == jobId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.LastStatus, lastStatus)
                    .SetProperty(x => x.LastError, lastError)
                    .SetProperty(x => x.FailureStreak, failureStreak),
                cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> TryClaimAsync(string jobId, DateTimeOffset scheduledFor, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        context.CronExecutions.Add(new CronExecutionEntity
        {
            JobId = jobId,
            ScheduledFor = scheduledFor,
            StartedAt = DateTimeOffset.UtcNow,
            State = "running",
        });

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            // (job_id, scheduled_for) 唯一约束冲突 = 该触发已经跑过
            return false;
        }
    }

    /// <inheritdoc />
    public async Task CompleteAsync(
        string jobId,
        DateTimeOffset scheduledFor,
        string state,
        string? result,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var execution = await context.CronExecutions
            .FirstOrDefaultAsync(x => x.JobId == jobId && x.ScheduledFor == scheduledFor, cancellationToken);
        if (execution is not null)
        {
            execution.FinishedAt = now;
            execution.State = state;
            execution.Result = result;
        }

        var job = await context.CronJobs.FirstOrDefaultAsync(x => x.Id == jobId, cancellationToken);
        if (job is not null)
        {
            job.LastRunAt = now;
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CronExecutionRecord>> ListExecutionsAsync(
        string jobId,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await context.CronExecutions.AsNoTracking()
            .Where(x => x.JobId == jobId)
            .OrderByDescending(x => x.ScheduledFor)
            .Take(Math.Clamp(limit, 1, 100))
            .ToListAsync(cancellationToken);

        return rows.Select(static row => new CronExecutionRecord(
                row.JobId,
                row.ScheduledFor,
                row.StartedAt,
                row.FinishedAt,
                row.State,
                row.Result))
            .ToList();
    }

    private static CronJobRecord ToRecord(CronJobEntity row) => new(
        row.Id,
        row.Name,
        row.Schedule,
        row.Kind,
        row.Payload,
        row.Deliver,
        row.Enabled,
        row.LastRunAt,
        row.CreatedAt,
        row.Mode,
        row.LaneId,
        row.SessionId,
        row.NextRunAt,
        row.LastStatus,
        row.LastError,
        row.FailureStreak);
}
