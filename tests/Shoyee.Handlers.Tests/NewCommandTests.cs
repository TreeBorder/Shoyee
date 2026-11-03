using Shoyee.Contracts.Abstractions;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;
using Shoyee.Handlers.Commands;
using Xunit;

namespace Shoyee.Handlers.Tests;

/// <summary><c>/new</c>：工作模式建会话并切指针；对话模式直接拒绝。</summary>
public sealed class NewCommandTests
{
    [Fact]
    public async Task Work_CreatesSessionAndSwitchesLanePointer()
    {
        var sessions = new FakeSessions();
        var command = new NewCommand(sessions);

        var reply = await command.HandleAsync(new CommandContext("mm:work", "ses_old", string.Empty, Mode.Work));

        var request = Assert.Single(sessions.Created);
        Assert.Equal(Mode.Work, request.Mode);
        Assert.Equal("mm:work", request.LaneId);
        Assert.Equal(("ses_new", "mm:work"), Assert.Single(sessions.Activated));
        Assert.Contains("新会话", reply);
    }

    [Fact]
    public async Task Roleplay_RefusesWithoutCreatingSession()
    {
        var sessions = new FakeSessions();
        var command = new NewCommand(sessions);

        var reply = await command.HandleAsync(new CommandContext("mm:roleplay", "mm:roleplay", string.Empty, Mode.Roleplay));

        Assert.Empty(sessions.Created);
        Assert.Empty(sessions.Activated);
        Assert.Contains("对话模式", reply);
    }

    private sealed class FakeSessions : ISessionService
    {
        public List<SessionCreateRequest> Created { get; } = [];

        public List<(string SessionId, string LaneId)> Activated { get; } = [];

        public Task<SessionDetail> CreateAsync(SessionCreateRequest request, CancellationToken cancellationToken = default)
        {
            Created.Add(request);
            return Task.FromResult(new SessionDetail(
                "ses_new", Mode.Work, LaneChannel.Desktop, request.LaneId, "新会话",
                false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 0, "work:ses_new", null, true));
        }

        public Task SetActiveForLaneAsync(string sessionId, string laneId, CancellationToken cancellationToken = default)
        {
            Activated.Add((sessionId, laneId));
            return Task.CompletedTask;
        }

        public Task<PageResult<SessionSummary>> ListAsync(SessionListRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SessionDetail> GetAsync(string sessionId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SessionDetail> OpenAsync(string sessionId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task RenameAsync(string sessionId, string title, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task ArchiveAsync(string sessionId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SessionMessagesResult> GetMessagesAsync(string sessionId, long? beforeId = null, long? afterId = null, int limit = 50, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SessionMessagesResult> GetLaneMessagesAsync(string laneId, long? beforeId = null, long? afterId = null, int limit = 50, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SessionSnapshot> GetSnapshotAsync(string sessionId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SessionExportResult> ExportAsync(SessionExportRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
