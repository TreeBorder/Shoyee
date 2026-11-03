using Shoyee.Contracts.Domain;
using Shoyee.Sessions.Lifecycle;
using Shoyee.Storage.Abstractions;
using Xunit;

namespace Shoyee.Sessions.Tests.Lifecycle;

public sealed class SessionFactoryTests
{
    private const string Lane = "mattermost:work:abc";
    private const string Key = "mattermost:dm:abc";

    private readonly InMemorySessionRepository _repository = new();
    private readonly SessionFactory _factory;

    public SessionFactoryTests()
    {
        var next = 0;
        _factory = new SessionFactory(_repository, () => "ses_" + ++next);
    }

    [Fact]
    public async Task GetOrCreateAsync_CreatesAndPointsLaneToNewSession()
    {
        var session = await _factory.GetOrCreateAsync(Key, LaneChannel.Mattermost, Mode.Work, Lane);

        Assert.Equal("ses_1", session.Id);
        Assert.Equal(Mode.Work, session.Mode);
        Assert.Equal(LaneChannel.Mattermost, session.Origin);
        Assert.Equal(Lane, session.LaneId);
        Assert.Null(session.ParentSessionId);
        Assert.Contains("会话 ", session.Title, StringComparison.Ordinal);
        Assert.Equal("ses_1", _repository.Active[Lane]);
    }

    [Fact]
    public async Task GetOrCreateAsync_ReusesActiveSession()
    {
        var first = await _factory.GetOrCreateAsync(Key, LaneChannel.Mattermost, Mode.Work, Lane);

        var second = await _factory.GetOrCreateAsync(Key, LaneChannel.Mattermost, Mode.Work, Lane);

        Assert.Equal(first.Id, second.Id);
        Assert.Single(_repository.Sessions);
    }

    [Fact]
    public async Task RotateIfStaleAsync_SameDay_OnlyTouches()
    {
        var at = Local(0, 9);
        var session = await _factory.GetOrCreateAsync(Key, LaneChannel.Mattermost, Mode.Work, Lane, at);

        var rotated = await _factory.RotateIfStaleAsync(
            Key, LaneChannel.Mattermost, Mode.Work, Lane, at.AddHours(3));

        Assert.Null(rotated);
        Assert.Single(_repository.Sessions);
        Assert.Equal(at.AddHours(3), _repository.Sessions[session.Id].LastActiveAt);
    }

    [Fact]
    public async Task RotateIfStaleAsync_CrossDay_ArchivesOldAndCreatesChild()
    {
        var at = Local(-1, 21);
        var old = await _factory.GetOrCreateAsync(Key, LaneChannel.Mattermost, Mode.Work, Lane, at);

        var rotated = await _factory.RotateIfStaleAsync(
            Key, LaneChannel.Mattermost, Mode.Work, Lane, Local(0, 9));

        Assert.NotNull(rotated);
        Assert.Equal(old.Id, rotated!.Id);
        Assert.True(rotated.Archived);
        Assert.True(_repository.Sessions[old.Id].Archived);

        var child = _repository.Sessions["ses_2"];
        Assert.False(child.Archived);
        Assert.Equal(old.Id, child.ParentSessionId);
        Assert.Equal("ses_2", _repository.Active[Lane]);
    }

    [Fact]
    public async Task RotateIfStaleAsync_NoActiveSession_ReturnsNull()
    {
        var rotated = await _factory.RotateIfStaleAsync(
            Key, LaneChannel.Mattermost, Mode.Work, Lane, DateTimeOffset.Now);

        Assert.Null(rotated);
        Assert.Empty(_repository.Sessions);
    }

    [Fact]
    public async Task ContinuityNote_PointsBackToRotatedSession()
    {
        var old = await _factory.GetOrCreateAsync(
            Key, LaneChannel.Mattermost, Mode.Work, Lane, Local(-1, 21));

        var note = SessionFactory.ContinuityNote(old);

        Assert.Contains(old.Id, note, StringComparison.Ordinal);
        Assert.Contains(old.Title!, note, StringComparison.Ordinal);
        Assert.Contains("session_search", note, StringComparison.Ordinal);
    }

    private static DateTimeOffset Local(int dayOffset, int hour)
        => new(DateTime.Today.AddDays(dayOffset).AddHours(hour));

    private sealed class InMemorySessionRepository : ISessionRepository
    {
        public Dictionary<string, SessionRecord> Sessions { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, string> Active { get; } = new(StringComparer.Ordinal);

        public Task<SessionRecord?> GetAsync(string sessionId, CancellationToken cancellationToken = default)
            => Task.FromResult(Sessions.TryGetValue(sessionId, out var session) ? session : null);

        public Task<SessionRecord?> FindActiveAsync(string laneId, CancellationToken cancellationToken = default)
        {
            if (Active.TryGetValue(laneId, out var id)
                && Sessions.TryGetValue(id, out var pointed)
                && !pointed.Archived)
            {
                return Task.FromResult<SessionRecord?>(pointed);
            }

            var fallback = Sessions.Values
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
            => Task.FromResult<IReadOnlyList<SessionRecord>>(Sessions.Values
                .Where(s => includeArchived || !s.Archived)
                .Where(s => mode is null || s.Mode == mode)
                .Where(s => laneId is null || s.LaneId == laneId)
                .OrderByDescending(s => s.LastActiveAt)
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
            => Task.FromResult<IReadOnlyList<SessionRecord>>(Sessions.Values
                .Where(s => includeArchived || !s.Archived)
                .Where(s => mode is null || s.Mode == mode)
                .Where(s => laneId is null || s.LaneId == laneId)
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
            Sessions[session.Id] = session;
            return Task.FromResult(session);
        }

        public Task RenameAsync(string sessionId, string title, CancellationToken cancellationToken = default)
        {
            if (Sessions.TryGetValue(sessionId, out var session))
            {
                Sessions[sessionId] = session with { Title = title };
            }

            return Task.CompletedTask;
        }

        public Task ArchiveAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            if (Sessions.TryGetValue(sessionId, out var session))
            {
                Sessions[sessionId] = session with { Archived = true };
            }

            return Task.CompletedTask;
        }

        public Task TouchAsync(string sessionId, DateTimeOffset at, CancellationToken cancellationToken = default)
        {
            if (Sessions.TryGetValue(sessionId, out var session))
            {
                Sessions[sessionId] = session with { LastActiveAt = at };
            }

            return Task.CompletedTask;
        }

        public Task SetActiveForLaneAsync(string laneId, string sessionId, CancellationToken cancellationToken = default)
        {
            Active[laneId] = sessionId;
            return Task.CompletedTask;
        }
    }
}
