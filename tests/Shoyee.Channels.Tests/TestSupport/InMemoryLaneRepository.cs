using Shoyee.Contracts.Domain;
using Shoyee.Storage.Abstractions;

namespace Shoyee.Channels.Tests.TestSupport;

/// <summary>内存 Lane 仓库：只实现管线用到的读与 touch。</summary>
internal sealed class InMemoryLaneRepository : ILaneRepository
{
    private readonly Dictionary<string, LaneRecord> _lanes = new(StringComparer.Ordinal);

    public List<(string LaneId, DateTimeOffset At)> Touched { get; } = [];

    public void Seed(LaneRecord lane) => _lanes[lane.Id] = lane;

    public Task<IReadOnlyList<LaneRecord>> ListAsync(Mode? mode = null, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<LaneRecord>>(
            _lanes.Values.Where(l => mode is null || l.Mode == mode).ToList());

    public Task<LaneRecord?> GetAsync(string laneId, CancellationToken cancellationToken = default)
        => Task.FromResult(_lanes.GetValueOrDefault(laneId));

    public Task<LaneRecord> UpsertAsync(LaneRecord lane, CancellationToken cancellationToken = default)
    {
        _lanes[lane.Id] = lane;
        return Task.FromResult(lane);
    }

    public Task SetActiveSessionAsync(string laneId, string? sessionId, CancellationToken cancellationToken = default)
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
        Touched.Add((laneId, at));
        if (_lanes.TryGetValue(laneId, out var lane))
        {
            _lanes[laneId] = lane with { LastTarget = target ?? lane.LastTarget };
        }

        return Task.CompletedTask;
    }

    public Task TouchInteractionAsync(string laneId, DateTimeOffset at, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<DateTimeOffset?> GetLastInteractionAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<DateTimeOffset?>(null);
}
