using Microsoft.EntityFrameworkCore;
using Shoyee.Contracts.Domain;
using Shoyee.Storage.Abstractions;
using Shoyee.Storage.Entities.Channel;

namespace Shoyee.Storage.Repositories;

/// <summary>Lane 仓库（纯 EF）。</summary>
public sealed class LaneRepository : ILaneRepository
{
    private readonly IDbContextFactory<ShoyeeDbContext> _contextFactory;

    /// <summary>创建仓库。</summary>
    public LaneRepository(IDbContextFactory<ShoyeeDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LaneRecord>> ListAsync(Mode? mode = null, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var query = context.Lanes.AsNoTracking();
        if (mode is { } value)
        {
            query = query.Where(x => x.Mode == value);
        }

        var rows = await query.OrderBy(x => x.Id).ToListAsync(cancellationToken);
        return rows.Select(ToRecord).ToList();
    }

    /// <inheritdoc />
    public async Task<LaneRecord?> GetAsync(string laneId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await context.Lanes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == laneId, cancellationToken);
        return row is null ? null : ToRecord(row);
    }

    /// <inheritdoc />
    public async Task<LaneRecord> UpsertAsync(LaneRecord lane, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lane.Id);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await context.Lanes.FirstOrDefaultAsync(x => x.Id == lane.Id, cancellationToken);
        if (row is null)
        {
            row = new LaneEntity { Id = lane.Id, CreatedAt = lane.CreatedAt == default ? DateTimeOffset.UtcNow : lane.CreatedAt };
            context.Lanes.Add(row);
        }

        row.Channel = lane.Channel;
        row.Mode = lane.Mode;
        row.ExternalKey = lane.ExternalKey;
        row.DisplayName = lane.DisplayName;
        row.ActiveSessionId = lane.ActiveSessionId;
        row.LastInboundAt = lane.LastInboundAt;
        row.LastInteractionAt = lane.LastInteractionAt;
        row.LastTarget = lane.LastTarget;

        await context.SaveChangesAsync(cancellationToken);
        return ToRecord(row);
    }

    /// <inheritdoc />
    public async Task SetActiveSessionAsync(string laneId, string? sessionId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await context.Lanes.FirstOrDefaultAsync(x => x.Id == laneId, cancellationToken);
        if (row is null)
        {
            return;
        }

        row.ActiveSessionId = sessionId;
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task TouchInboundAsync(
        string laneId,
        DateTimeOffset at,
        string? target = null,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await context.Lanes.FirstOrDefaultAsync(x => x.Id == laneId, cancellationToken);
        if (row is null)
        {
            return;
        }

        row.LastInboundAt = at;
        row.LastInteractionAt = at;
        if (target is not null)
        {
            row.LastTarget = target;
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task TouchInteractionAsync(string laneId, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await context.Lanes.FirstOrDefaultAsync(x => x.Id == laneId, cancellationToken);
        if (row is null)
        {
            return;
        }

        row.LastInteractionAt = at;
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<DateTimeOffset?> GetLastInteractionAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var values = await context.Lanes.AsNoTracking()
            .Where(x => x.LastInteractionAt != null)
            .Select(x => x.LastInteractionAt)
            .ToListAsync(cancellationToken);
        return values.Count == 0 ? null : values.Max();
    }

    private static LaneRecord ToRecord(LaneEntity row) => new(
        row.Id,
        row.Channel,
        row.Mode,
        row.ExternalKey,
        row.DisplayName,
        row.ActiveSessionId,
        row.LastInboundAt,
        row.LastInteractionAt,
        row.CreatedAt,
        row.LastTarget);
}
