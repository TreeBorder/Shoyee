using Shoyee.Contracts.Abstractions;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;

namespace Shoyee.Channels.Tests.TestSupport;

/// <summary>只记录「建会话 / 设指针」的假会话服务。</summary>
internal sealed class StubSessionService : ISessionService
{
    public List<SessionCreateRequest> Created { get; } = [];

    public List<(string SessionId, string LaneId)> Activated { get; } = [];

    public Task<SessionDetail> CreateAsync(SessionCreateRequest request, CancellationToken cancellationToken = default)
    {
        Created.Add(request);
        return Task.FromResult(Detail("ses_" + Created.Count, request.LaneId));
    }

    public Task SetActiveForLaneAsync(string sessionId, string laneId, CancellationToken cancellationToken = default)
    {
        Activated.Add((sessionId, laneId));
        return Task.CompletedTask;
    }

    public Task<PageResult<SessionSummary>> ListAsync(
        SessionListRequest request,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new PageResult<SessionSummary>([]));

    public Task<SessionDetail> GetAsync(string sessionId, CancellationToken cancellationToken = default)
        => Task.FromResult(Detail(sessionId, null));

    public Task<SessionDetail> OpenAsync(string sessionId, CancellationToken cancellationToken = default)
        => Task.FromResult(Detail(sessionId, null));

    public Task RenameAsync(string sessionId, string title, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task ArchiveAsync(string sessionId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<SessionMessagesResult> GetMessagesAsync(
        string sessionId,
        long? beforeId = null,
        long? afterId = null,
        int limit = 50,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new SessionMessagesResult([], false));

    public Task<SessionMessagesResult> GetLaneMessagesAsync(
        string laneId,
        long? beforeId = null,
        long? afterId = null,
        int limit = 50,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new SessionMessagesResult([], false));

    public Task<SessionSnapshot> GetSnapshotAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new SessionSnapshot(string.Empty));

    public Task<SessionExportResult> ExportAsync(
        SessionExportRequest request,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    private static SessionDetail Detail(string id, string? laneId) => new(
        id,
        Mode.Work,
        LaneChannel.Mattermost,
        laneId,
        "标题",
        Archived: false,
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch,
        MessageCount: 0,
        SessionKey: id,
        ParentSessionId: null,
        IsActiveForLane: true);
}
