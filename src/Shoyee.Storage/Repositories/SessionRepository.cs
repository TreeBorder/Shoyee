using Microsoft.EntityFrameworkCore;
using Shoyee.Contracts.Domain;
using Shoyee.Storage.Abstractions;
using Shoyee.Storage.Entities.Session;

namespace Shoyee.Storage.Repositories;

/// <summary>工作会话仓库（纯 EF）。</summary>
public sealed class SessionRepository : ISessionRepository
{
    private readonly IDbContextFactory<ShoyeeDbContext> _contextFactory;

    /// <summary>创建仓库。</summary>
    public SessionRepository(IDbContextFactory<ShoyeeDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    /// <inheritdoc />
    public async Task<SessionRecord?> GetAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await context.Sessions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == sessionId, cancellationToken);
        return row is null ? null : ToRecord(row);
    }

    /// <inheritdoc />
    public async Task<SessionRecord?> FindActiveAsync(string laneId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var lane = await context.Lanes.AsNoTracking()
            .Where(x => x.Id == laneId)
            .Select(x => x.ActiveSessionId)
            .FirstOrDefaultAsync(cancellationToken);

        if (!string.IsNullOrEmpty(lane))
        {
            var pointed = await context.Sessions.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == lane && !x.Archived, cancellationToken);
            if (pointed is not null)
            {
                return ToRecord(pointed);
            }
        }

        var fallback = await context.Sessions.AsNoTracking()
            .Where(x => x.LaneId == laneId && !x.Archived)
            .OrderByDescending(x => x.LastActiveAt)
            .FirstOrDefaultAsync(cancellationToken);
        return fallback is null ? null : ToRecord(fallback);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SessionRecord>> ListAsync(
        Mode? mode = null,
        string? laneId = null,
        bool includeArchived = false,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var query = context.Sessions.AsNoTracking();
        if (mode is { } value)
        {
            query = query.Where(x => x.Mode == value);
        }

        if (laneId is not null)
        {
            query = query.Where(x => x.LaneId == laneId);
        }

        if (!includeArchived)
        {
            query = query.Where(x => !x.Archived);
        }

        var rows = await query.OrderByDescending(x => x.LastActiveAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return rows.Select(ToRecord).ToList();
    }

    /// <summary>
    /// 游标分页：按 (LastActiveAt desc, Id desc) keyset，避免 offset 在活跃会话插入时漏条/重条。
    /// </summary>
    public async Task<IReadOnlyList<SessionRecord>> ListPageAsync(
        Mode? mode,
        string? laneId,
        bool includeArchived,
        DateTimeOffset? beforeLastActiveAt,
        string? beforeId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var query = context.Sessions.AsNoTracking();
        if (mode is { } value)
        {
            query = query.Where(x => x.Mode == value);
        }

        if (laneId is not null)
        {
            query = query.Where(x => x.LaneId == laneId);
        }

        if (!includeArchived)
        {
            query = query.Where(x => !x.Archived);
        }

        if (beforeLastActiveAt is { } at && beforeId is not null)
        {
            query = query.Where(x => x.LastActiveAt < at || (x.LastActiveAt == at && string.Compare(x.Id, beforeId) < 0));
        }

        var rows = await query
            .OrderByDescending(x => x.LastActiveAt)
            .ThenByDescending(x => x.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return rows.Select(ToRecord).ToList();
    }

    /// <inheritdoc />
    public async Task<SessionRecord> CreateAsync(SessionRecord session, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(session.Id);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var row = new SessionEntity
        {
            Id = session.Id,
            Mode = session.Mode,
            Origin = session.Origin,
            LaneId = session.LaneId,
            SessionKey = session.SessionKey,
            Title = session.Title,
            ParentSessionId = session.ParentSessionId,
            CreatedAt = session.CreatedAt == default ? now : session.CreatedAt,
            LastActiveAt = session.LastActiveAt == default ? now : session.LastActiveAt,
            Archived = session.Archived,
        };
        context.Sessions.Add(row);
        await context.SaveChangesAsync(cancellationToken);
        return ToRecord(row);
    }

    /// <inheritdoc />
    public async Task RenameAsync(string sessionId, string title, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await context.Sessions.FirstOrDefaultAsync(x => x.Id == sessionId, cancellationToken);
        if (row is null)
        {
            return;
        }

        row.Title = title;
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task ArchiveAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await context.Sessions.FirstOrDefaultAsync(x => x.Id == sessionId, cancellationToken);
        if (row is null)
        {
            return;
        }

        row.Archived = true;
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task TouchAsync(string sessionId, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await context.Sessions.FirstOrDefaultAsync(x => x.Id == sessionId, cancellationToken);
        if (row is null)
        {
            return;
        }

        row.LastActiveAt = at;
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task SetActiveForLaneAsync(string laneId, string sessionId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var lane = await context.Lanes.FirstOrDefaultAsync(x => x.Id == laneId, cancellationToken);
        if (lane is null)
        {
            return;
        }

        lane.ActiveSessionId = sessionId;
        await context.SaveChangesAsync(cancellationToken);
    }

    private static SessionRecord ToRecord(SessionEntity row) => new(
        row.Id,
        row.Mode,
        row.Origin,
        row.LaneId,
        row.SessionKey,
        row.Title,
        row.ParentSessionId,
        row.CreatedAt,
        row.LastActiveAt,
        row.Archived);
}
