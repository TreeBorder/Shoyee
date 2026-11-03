using Shoyee.Contracts.Domain;
using Shoyee.Storage.Abstractions;

namespace Shoyee.Agent.Tests.TestSupport;

/// <summary>内存版工作会话仓库（只实现轮次/会话服务测试用得到的行为）。</summary>
internal sealed class InMemorySessionRepository : ISessionRepository
{
    private readonly Dictionary<string, SessionRecord> _sessions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _active = new(StringComparer.Ordinal);

    public void Seed(SessionRecord session) => _sessions[session.Id] = session;

    public Task<SessionRecord?> GetAsync(string sessionId, CancellationToken cancellationToken = default)
        => Task.FromResult(_sessions.TryGetValue(sessionId, out var session) ? session : null);

    public Task<SessionRecord?> FindActiveAsync(string laneId, CancellationToken cancellationToken = default)
    {
        if (_active.TryGetValue(laneId, out var id)
            && _sessions.TryGetValue(id, out var pointed)
            && !pointed.Archived)
        {
            return Task.FromResult<SessionRecord?>(pointed);
        }

        var fallback = _sessions.Values
            .Where(s => s.LaneId == laneId && !s.Archived)
            .OrderByDescending(s => s.LastActiveAt)
            .FirstOrDefault();
        return Task.FromResult(fallback);
    }

    public Task<IReadOnlyList<SessionRecord>> ListAsync(
        Mode? mode = null,
        string? laneId = null,
        bool includeArchived = false,
        int limit = 50,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<SessionRecord>>(_sessions.Values
            .Where(s => mode is null || s.Mode == mode)
            .Where(s => laneId is null || s.LaneId == laneId)
            .Where(s => includeArchived || !s.Archived)
            .OrderByDescending(s => s.LastActiveAt)
            .ThenByDescending(s => s.Id, StringComparer.Ordinal)
            .Take(limit)
            .ToList());

    public Task<IReadOnlyList<SessionRecord>> ListPageAsync(
        Mode? mode,
        string? laneId,
        bool includeArchived,
        DateTimeOffset? beforeLastActiveAt,
        string? beforeId,
        int limit,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<SessionRecord>>(_sessions.Values
            .Where(s => mode is null || s.Mode == mode)
            .Where(s => laneId is null || s.LaneId == laneId)
            .Where(s => includeArchived || !s.Archived)
            .Where(s => beforeLastActiveAt is null || beforeId is null
                        || s.LastActiveAt < beforeLastActiveAt
                        || (s.LastActiveAt == beforeLastActiveAt
                            && string.Compare(s.Id, beforeId, StringComparison.Ordinal) < 0))
            .OrderByDescending(s => s.LastActiveAt)
            .ThenByDescending(s => s.Id, StringComparer.Ordinal)
            .Take(limit)
            .ToList());

    public Task<SessionRecord> CreateAsync(SessionRecord session, CancellationToken cancellationToken = default)
    {
        _sessions[session.Id] = session;
        return Task.FromResult(session);
    }

    public Task RenameAsync(string sessionId, string title, CancellationToken cancellationToken = default)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            _sessions[sessionId] = session with { Title = title };
        }

        return Task.CompletedTask;
    }

    public Task ArchiveAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            _sessions[sessionId] = session with { Archived = true };
        }

        return Task.CompletedTask;
    }

    public Task TouchAsync(string sessionId, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            _sessions[sessionId] = session with { LastActiveAt = at };
        }

        return Task.CompletedTask;
    }

    public Task SetActiveForLaneAsync(string laneId, string sessionId, CancellationToken cancellationToken = default)
    {
        _active[laneId] = sessionId;
        return Task.CompletedTask;
    }
}
