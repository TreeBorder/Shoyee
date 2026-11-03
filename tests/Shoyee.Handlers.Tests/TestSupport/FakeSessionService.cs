using Shoyee.Contracts.Abstractions;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;

namespace Shoyee.Handlers.Tests.TestSupport;

/// <summary>可控的会话服务替身：列表由测试给定，记录打开/切指针/改名。</summary>
internal sealed class FakeSessionService : ISessionService
{
    public List<SessionSummary> Items { get; } = [];

    public List<string> Opened { get; } = [];

    public List<(string SessionId, string LaneId)> Activated { get; } = [];

    public List<(string SessionId, string Title)> Renamed { get; } = [];

    public int ListCalls { get; private set; }

    public Task<PageResult<SessionSummary>> ListAsync(
        SessionListRequest request,
        CancellationToken cancellationToken = default)
    {
        ListCalls++;
        return Task.FromResult(new PageResult<SessionSummary>(Items));
    }

    public Task<SessionDetail> OpenAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        Opened.Add(sessionId);
        return Task.FromResult(Detail(sessionId));
    }

    public Task SetActiveForLaneAsync(
        string sessionId,
        string laneId,
        CancellationToken cancellationToken = default)
    {
        Activated.Add((sessionId, laneId));
        return Task.CompletedTask;
    }

    public Task RenameAsync(string sessionId, string title, CancellationToken cancellationToken = default)
    {
        Renamed.Add((sessionId, title));
        return Task.CompletedTask;
    }

    public Task<SessionDetail> CreateAsync(SessionCreateRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(Detail("ses_new"));

    public Task<SessionDetail> GetAsync(string sessionId, CancellationToken cancellationToken = default)
        => Task.FromResult(Detail(sessionId));

    public Task ArchiveAsync(string sessionId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<SessionMessagesResult> GetMessagesAsync(
        string sessionId,
        long? beforeId = null,
        long? afterId = null,
        int limit = 50,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<SessionMessagesResult> GetLaneMessagesAsync(
        string laneId,
        long? beforeId = null,
        long? afterId = null,
        int limit = 50,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<SessionSnapshot> GetSnapshotAsync(string sessionId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<SessionExportResult> ExportAsync(
        SessionExportRequest request,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    private static SessionDetail Detail(string sessionId) => new(
        sessionId,
        Mode.Work,
        LaneChannel.Mattermost,
        "mm:work",
        "会话",
        Archived: false,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow,
        0,
        "work:" + sessionId,
        null,
        true);
}
