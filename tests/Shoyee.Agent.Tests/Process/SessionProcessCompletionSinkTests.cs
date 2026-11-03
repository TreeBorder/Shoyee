using Microsoft.Extensions.Logging.Abstractions;
using Shoyee.Agent.Process;
using Shoyee.Agent.Tests.TestSupport;
using Shoyee.Contracts.Abstractions;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;
using Shoyee.Storage.Abstractions;
using Shoyee.Tools.Process;
using Xunit;

namespace Shoyee.Agent.Tests.Process;

public sealed class SessionProcessCompletionSinkTests
{
    [Fact]
    public async Task Completion_IsInjectedAsSystemTurn_ForDesktopSession()
    {
        var sessions = new InMemorySessionRepository();
        sessions.Seed(Session("s-1", LaneChannel.Desktop, laneId: "desktop:work"));
        var lanes = new InMemoryLaneRepository();
        lanes.Seed(Lane("desktop:work", LaneChannel.Desktop, target: null));
        var turns = new RecordingTurnService();
        var sink = new SessionProcessCompletionSink(
            new Lazy<ITurnService>(() => turns), sessions, lanes, NullLogger<SessionProcessCompletionSink>.Instance);

        await sink.NotifyAsync(Completion("s-1", "build", "done", 0, "ok"));

        var request = Assert.Single(turns.Requests);
        Assert.Equal("s-1", request.SessionId);
        Assert.True(request.SystemInput);
        Assert.Null(request.ReplyTarget);
        Assert.Contains("p-123", request.Text);
        Assert.Contains("已完成", request.Text);
        Assert.Contains("build", request.Text);
        Assert.Contains("输出尾部", request.Text);
        Assert.StartsWith("[系统消息]", request.Text);
        Assert.EndsWith("[/]", request.Text);
    }

    [Fact]
    public async Task Completion_RepliesToLaneTarget_ForImSession()
    {
        var sessions = new InMemorySessionRepository();
        sessions.Seed(Session("s-2", LaneChannel.Mattermost, laneId: "mattermost:work"));
        var lanes = new InMemoryLaneRepository();
        lanes.Seed(Lane("mattermost:work", LaneChannel.Mattermost, target: "chan/root"));
        var turns = new RecordingTurnService();
        var sink = new SessionProcessCompletionSink(
            new Lazy<ITurnService>(() => turns), sessions, lanes, NullLogger<SessionProcessCompletionSink>.Instance);

        await sink.NotifyAsync(Completion("s-2", "build", "failed", 2, ""));

        Assert.Equal("chan/root", Assert.Single(turns.Requests).ReplyTarget);
    }

    [Fact]
    public async Task MissingOrArchivedSession_IsDropped()
    {
        var sessions = new InMemorySessionRepository();
        sessions.Seed(Session("s-3", LaneChannel.Desktop, laneId: "desktop:work") with { Archived = true });
        var turns = new RecordingTurnService();
        var sink = new SessionProcessCompletionSink(
            new Lazy<ITurnService>(() => turns), sessions, new InMemoryLaneRepository(),
            NullLogger<SessionProcessCompletionSink>.Instance);

        await sink.NotifyAsync(Completion("s-3", "build", "done", 0, "ok"));
        await sink.NotifyAsync(Completion("nope", "build", "done", 0, "ok"));

        Assert.Empty(turns.Requests);
    }

    private static ProcessCompletion Completion(string sessionKey, string command, string status, int exitCode, string tail)
        => new("p-123", sessionKey, command, null, status, exitCode, tail);

    private static SessionRecord Session(string id, LaneChannel origin, string laneId)
        => new(
            id,
            Mode.Work,
            origin,
            laneId,
            "key-" + id,
            "标题",
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            Archived: false);

    private static LaneRecord Lane(string id, LaneChannel channel, string? target)
        => new(
            id,
            channel,
            Mode.Work,
            "external",
            "展示名",
            null,
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            target);

    private sealed class RecordingTurnService : ITurnService
    {
        public List<TurnSendRequest> Requests { get; } = [];

        public Task<TurnRef> SendAsync(TurnSendRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new TurnRef("t-" + Requests.Count));
        }

        public Task<TurnOutcome> SendAndWaitAsync(TurnSendRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<TurnRef> RetryAsync(string sessionId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<TurnOutcome> AppendSpokenAsync(
            string laneId,
            string text,
            IReadOnlyList<MessageAttachment>? attachments = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task CancelAsync(string sessionId, string? turnId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RespondApprovalAsync(ApprovalRespondRequest request, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
