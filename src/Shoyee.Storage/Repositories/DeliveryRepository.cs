using Microsoft.EntityFrameworkCore;
using Shoyee.Contracts.Domain;
using Shoyee.Storage.Abstractions;
using Shoyee.Storage.Entities.Channel;

namespace Shoyee.Storage.Repositories;

/// <summary>投递台账仓库（纯 EF）。</summary>
public sealed class DeliveryRepository : IDeliveryRepository
{
    private readonly IDbContextFactory<ShoyeeDbContext> _contextFactory;

    /// <summary>创建仓库。</summary>
    public DeliveryRepository(IDbContextFactory<ShoyeeDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    /// <inheritdoc />
    public async Task<DeliveryRecord> EnqueueAsync(
        string laneId,
        string? sessionId,
        string kind,
        string payloadJson,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await context.Deliveries
            .Where(x => x.LaneId == laneId && x.Kind == kind && x.PayloadJson == payloadJson)
            .Where(x => x.State == DeliveryState.Pending || x.State == DeliveryState.Failed)
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (existing is not null)
        {
            return ToRecord(existing);
        }

        var now = DateTimeOffset.UtcNow;
        var row = new DeliveryEntity
        {
            LaneId = laneId,
            SessionId = sessionId,
            Kind = kind,
            PayloadJson = payloadJson,
            State = DeliveryState.Pending,
            Attempts = 0,
            CreatedAt = now,
            UpdatedAt = now,
        };
        context.Deliveries.Add(row);
        await context.SaveChangesAsync(cancellationToken);
        return ToRecord(row);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DeliveryRecord>> GetDueAsync(
        DateTimeOffset now,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await context.Deliveries.AsNoTracking()
            .Where(x => x.State == DeliveryState.Pending || x.State == DeliveryState.Failed)
            .OrderBy(x => x.NextAttemptAt ?? x.CreatedAt)
            .Take(limit * 2)
            .ToListAsync(cancellationToken);

        return rows
            .Where(x => x.NextAttemptAt is null || x.NextAttemptAt <= now)
            .Take(limit)
            .Select(ToRecord)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DeliveryRecord>> GetPendingForLaneAsync(
        string laneId,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await context.Deliveries.AsNoTracking()
            .Where(x => x.LaneId == laneId)
            .Where(x => x.State == DeliveryState.Pending || x.State == DeliveryState.Failed)
            .OrderBy(x => x.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return rows.Select(ToRecord).ToList();
    }

    /// <inheritdoc />
    public async Task MarkSentAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await context.Deliveries.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (row is null)
        {
            return;
        }

        row.State = DeliveryState.Sent;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        row.LastError = null;
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task MarkFailedAsync(
        long id,
        string error,
        DateTimeOffset nextAttemptAt,
        int maxAttempts = 10,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await context.Deliveries.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (row is null)
        {
            return;
        }

        row.Attempts++;
        row.LastError = error;
        row.NextAttemptAt = nextAttemptAt;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        row.State = row.Attempts >= maxAttempts ? DeliveryState.Abandoned : DeliveryState.Failed;
        await context.SaveChangesAsync(cancellationToken);
    }

    private static DeliveryRecord ToRecord(DeliveryEntity row) => new(
        row.Id,
        row.LaneId,
        row.SessionId,
        row.Kind,
        row.PayloadJson,
        row.State,
        row.Attempts,
        row.NextAttemptAt,
        row.LastError,
        row.CreatedAt,
        row.UpdatedAt);
}
