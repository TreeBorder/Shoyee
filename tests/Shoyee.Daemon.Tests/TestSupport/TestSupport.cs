using Xunit;
using Shoyee.Contracts.Domain;
using Shoyee.Storage.Abstractions;

namespace Shoyee.Daemon.Tests.TestSupport;

/// <summary>内存 Lane 仓库（够测试播种语义）。</summary>
internal sealed class InMemoryLaneRepository : ILaneRepository
{
    private readonly Dictionary<string, LaneRecord> _lanes = new(StringComparer.Ordinal);

    public IReadOnlyCollection<LaneRecord> All => _lanes.Values;

    public Task<IReadOnlyList<LaneRecord>> ListAsync(
        Mode? mode = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<LaneRecord>>(
            _lanes.Values.Where(lane => mode is null || lane.Mode == mode).ToList());

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
            _lanes[laneId] = lane with
            {
                LastInboundAt = at,
                LastInteractionAt = at,
                LastTarget = target ?? lane.LastTarget,
            };
        }

        return Task.CompletedTask;
    }

    public Task TouchInteractionAsync(
        string laneId,
        DateTimeOffset at,
        CancellationToken cancellationToken = default)
    {
        if (_lanes.TryGetValue(laneId, out var lane))
        {
            _lanes[laneId] = lane with { LastInteractionAt = at };
        }

        return Task.CompletedTask;
    }

    public Task<DateTimeOffset?> GetLastInteractionAsync(CancellationToken cancellationToken = default)
    {
        var values = _lanes.Values
            .Where(static lane => lane.LastInteractionAt is not null)
            .Select(static lane => lane.LastInteractionAt)
            .ToList();
        return Task.FromResult(values.Count == 0 ? null : values.Max());
    }
}

/// <summary>临时目录。</summary>
internal sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "shoyee-daemon-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }
}
