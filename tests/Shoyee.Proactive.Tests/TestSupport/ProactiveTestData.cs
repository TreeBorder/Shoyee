using Shoyee.Contracts.Abstractions;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Options;
using Shoyee.Storage.Abstractions;

namespace Shoyee.Proactive.Tests.TestSupport;

/// <summary>内存 Lane 仓库。</summary>
internal sealed class FakeLaneRepository(params LaneRecord[] lanes) : ILaneRepository
{
    private readonly List<LaneRecord> _lanes = [.. lanes];

    public Task<IReadOnlyList<LaneRecord>> ListAsync(
        Mode? mode = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<LaneRecord>>(
            _lanes.Where(lane => mode is null || lane.Mode == mode).ToList());

    public Task<LaneRecord?> GetAsync(string laneId, CancellationToken cancellationToken = default)
        => Task.FromResult(_lanes.FirstOrDefault(lane => lane.Id == laneId));

    public Task<LaneRecord> UpsertAsync(LaneRecord lane, CancellationToken cancellationToken = default)
    {
        _lanes.RemoveAll(existing => existing.Id == lane.Id);
        _lanes.Add(lane);
        return Task.FromResult(lane);
    }

    public Task SetActiveSessionAsync(
        string laneId,
        string? sessionId,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task TouchInboundAsync(
        string laneId,
        DateTimeOffset at,
        string? target = null,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task TouchInteractionAsync(
        string laneId,
        DateTimeOffset at,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<DateTimeOffset?> GetLastInteractionAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_lanes.Max(lane => lane.LastInteractionAt));
}

/// <summary>内存状态仓库。</summary>
internal sealed class FakeStateRepository : IStateRepository
{
    public Dictionary<string, StateRecord> Rows { get; } = new(StringComparer.Ordinal);

    public Task<IReadOnlyList<StateRecord>> GetAllAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<StateRecord>>(Rows.Values.ToList());

    public Task<StateRecord?> GetAsync(string key, CancellationToken cancellationToken = default)
        => Task.FromResult(Rows.TryGetValue(key, out var row) ? row : null);

    public Task<bool> SetAsync(
        string key,
        string value,
        bool byAgent = false,
        CancellationToken cancellationToken = default)
    {
        if (!Rows.TryGetValue(key, out var row))
        {
            return Task.FromResult(false);
        }

        if (byAgent && !row.AgentEditable)
        {
            return Task.FromResult(false);
        }

        Rows[key] = row with { Value = value, UpdatedAt = DateTimeOffset.UtcNow };
        return Task.FromResult(true);
    }

    public Task<StateRecord> UpsertAsync(StateRecord record, CancellationToken cancellationToken = default)
    {
        Rows[record.Key] = record;
        return Task.FromResult(record);
    }

    public Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
        => Task.FromResult(Rows.Remove(key));

    public Task SeedIfEmptyAsync(
        IReadOnlyList<StateRecord> defaults,
        CancellationToken cancellationToken = default)
    {
        if (Rows.Count > 0)
        {
            return Task.CompletedTask;
        }

        foreach (var row in defaults)
        {
            Rows[row.Key] = row;
        }

        return Task.CompletedTask;
    }
}

/// <summary>记录轮次请求的假 TurnService。</summary>
internal sealed class RecordingTurnService : ITurnService
{
    public List<TurnSendRequest> Requests { get; } = [];

    public TurnOutcome Outcome { get; set; } = new("trn_1", true, "我在呢");

    public Task<TurnRef> SendAsync(TurnSendRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        return Task.FromResult(new TurnRef(Outcome.TurnId));
    }

    public Task<TurnRef> RetryAsync(string sessionId, CancellationToken cancellationToken = default)
        => Task.FromResult(new TurnRef(Outcome.TurnId));

    public Task<TurnOutcome> SendAndWaitAsync(
        TurnSendRequest request,
        CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        return Task.FromResult(Outcome);
    }

    public List<(string LaneId, string Text)> Spoken { get; } = [];

    public Task<TurnOutcome> AppendSpokenAsync(
        string laneId,
        string text,
        IReadOnlyList<MessageAttachment>? attachments = null,
        CancellationToken cancellationToken = default)
    {
        Spoken.Add((laneId, text));
        return Task.FromResult(new TurnOutcome("spk_1", true, text, null, attachments));
    }

    public Task CancelAsync(string sessionId, string? turnId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task RespondApprovalAsync(
        ApprovalRespondRequest request,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

/// <summary>记录出站投递的假实现。</summary>
internal sealed class RecordingDelivery : IOutboundDelivery
{
    public List<(string LaneId, string? SessionId, string Target, string Text, string Kind)> Sent { get; } = [];

    public List<(string LaneId, string? SessionId, string Target, string FilePath, string? Caption, string Kind)> Files { get; } = [];

    public Task SendTextAsync(
        string laneId,
        string? sessionId,
        string target,
        string text,
        string kind = "message",
        CancellationToken cancellationToken = default)
    {
        Sent.Add((laneId, sessionId, target, text, kind));
        return Task.CompletedTask;
    }

    public Task SendFileAsync(
        string laneId,
        string? sessionId,
        string target,
        string filePath,
        string? caption = null,
        string kind = "file",
        CancellationToken cancellationToken = default)
    {
        Files.Add((laneId, sessionId, target, filePath, caption, kind));
        return Task.CompletedTask;
    }
}

/// <summary>可控的在线判定。</summary>
internal sealed class FakePresence : IClientPresence
{
    public HashSet<ClientRole> Online { get; } = [];

    public bool IsOnline(ClientRole role) => Online.Contains(role);
}

internal static class ProactiveTestData
{
    public static readonly DateTimeOffset Now = new(2026, 10, 29, 14, 0, 0, TimeSpan.Zero);

    public static WorkspaceConfig Config(string root, HeartbeatOptions? heartbeat = null)
        => new(
            root,
            new ModelProfiles(
                new ModelEndpoint("http://localhost:1/v1", "main", "MAIN_KEY"),
                new ModelEndpoint("http://localhost:1/v1", "small", "SMALL_KEY")),
            new ChannelAccounts(),
            new ProactiveOptions(heartbeat ?? new HeartbeatOptions(Enabled: true)),
            new ToolOptions());

    public static LaneRecord Desktop(DateTimeOffset? last = null) => new(
        LaneIds.DesktopRoleplay,
        LaneChannel.Desktop,
        Mode.Roleplay,
        "desktop",
        "桌面",
        null,
        last,
        last,
        Now.AddDays(-10));

    public static LaneRecord Im(
        string id,
        DateTimeOffset? lastInteraction,
        string? lastTarget) => new(
        id,
        LaneChannel.Mattermost,
        Mode.Roleplay,
        "http://mm",
        "手机",
        null,
        lastInteraction,
        lastInteraction,
        Now.AddDays(-10),
        lastTarget);
}
