using Shoyee.Contracts.Domain;
using Shoyee.Storage.Abstractions;

namespace Shoyee.Agent.Tests.TestSupport;

/// <summary>内存版 Lane 仓库。</summary>
internal sealed class InMemoryLaneRepository : ILaneRepository
{
    private readonly Dictionary<string, LaneRecord> _lanes = new(StringComparer.Ordinal);

    public void Seed(LaneRecord lane) => _lanes[lane.Id] = lane;

    public Task<IReadOnlyList<LaneRecord>> ListAsync(
        Mode? mode = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<LaneRecord>>(
            _lanes.Values.Where(l => mode is null || l.Mode == mode).ToList());

    public Task<LaneRecord?> GetAsync(string laneId, CancellationToken cancellationToken = default)
        => Task.FromResult(_lanes.TryGetValue(laneId, out var lane) ? lane : null);

    public Task<LaneRecord> UpsertAsync(LaneRecord lane, CancellationToken cancellationToken = default)
    {
        _lanes[lane.Id] = lane;
        return Task.FromResult(lane);
    }

    public Task SetActiveSessionAsync(
        string laneId,
        string? sessionId,
        CancellationToken cancellationToken = default)
    {
        if (_lanes.TryGetValue(laneId, out var lane))
        {
            _lanes[laneId] = lane with { ActiveSessionId = sessionId };
        }

        return Task.CompletedTask;
    }

    public Task TouchInboundAsync(
        string laneId,
        DateTimeOffset at,
        string? target = null,
        CancellationToken cancellationToken = default)
    {
        if (_lanes.TryGetValue(laneId, out var lane))
        {
            _lanes[laneId] = lane with { LastInboundAt = at, LastInteractionAt = at };
        }

        return Task.CompletedTask;
    }

    public Task TouchInteractionAsync(string laneId, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        if (_lanes.TryGetValue(laneId, out var lane))
        {
            _lanes[laneId] = lane with { LastInteractionAt = at };
        }

        return Task.CompletedTask;
    }

    public Task<DateTimeOffset?> GetLastInteractionAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_lanes.Values.Select(l => l.LastInteractionAt).Max());
}
