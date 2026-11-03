using Shoyee.Contracts.Abstractions;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Options;
using Shoyee.Proactive.Cron;
using Shoyee.Proactive.Tests.TestSupport;
using Shoyee.Storage.Abstractions;
using Xunit;

namespace Shoyee.Proactive.Tests.Cron;

public sealed class CronRunnerTests
{
    private static readonly DateTimeOffset Scheduled = new(2026, 10, 29, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task WorkMessage_RecordsResult_WithoutTouchingConversation()
    {
        var (runner, jobs, turns, delivery, _) = Create();
        var job = Job(Mode.Work, "message", "早上的日报模板");
        await jobs.UpsertAsync(job);

        await runner.ExecuteAsync(job.Id, Scheduled);

        var execution = Assert.Single(await jobs.ListExecutionsAsync(job.Id));
        Assert.Equal("succeeded", execution.State);
        Assert.Equal("早上的日报模板", execution.Result);
        Assert.Empty(turns.Spoken);
        Assert.Empty(turns.Requests);
        Assert.Empty(delivery.Sent);
        Assert.Equal("succeeded", (await jobs.GetAsync(job.Id))!.LastStatus);
    }

    [Fact]
    public async Task RoleplayMessage_IsSpokenOnLane_AndDeliveredToIm()
    {
        var (runner, jobs, turns, delivery, _) = Create(
            lanes: [new LaneRecord(
                "mattermost:roleplay",
                LaneChannel.Mattermost,
                Mode.Roleplay,
                "mm",
                "手机",
                null,
                null,
                null,
                DateTimeOffset.UtcNow,
                "mm-target")]);
        var job = Job(Mode.Roleplay, "message", "早上好呀", laneId: "mattermost:roleplay");
        await jobs.UpsertAsync(job);

        await runner.ExecuteAsync(job.Id, Scheduled);

        var spoken = Assert.Single(turns.Spoken);
        Assert.Equal("mattermost:roleplay", spoken.LaneId);
        Assert.Equal("早上好呀", spoken.Text);
        var sent = Assert.Single(delivery.Sent);
        Assert.Equal("mm-target", sent.Target);
        Assert.Equal("cron", sent.Kind);
    }

    [Fact]
    public async Task RoleplayPrompt_DeliversAttachmentsToIm()
    {
        var (runner, jobs, turns, delivery, _) = Create(
            lanes: [new LaneRecord(
                "mattermost:roleplay",
                LaneChannel.Mattermost,
                Mode.Roleplay,
                "mm",
                "手机",
                null,
                null,
                null,
                DateTimeOffset.UtcNow,
                "mm-target")]);
        turns.Outcome = new TurnOutcome(
            "trn_1",
            true,
            "给你拍了张",
            null,
            [new MessageAttachment("/tmp/a.png", "a.png", "image/png", 10, AttachmentKind.Image)]);
        var job = Job(Mode.Roleplay, "prompt", "拍张照", laneId: "mattermost:roleplay");
        await jobs.UpsertAsync(job);

        await runner.ExecuteAsync(job.Id, Scheduled);

        var text = Assert.Single(delivery.Sent);
        Assert.Equal("给你拍了张", text.Text);
        var file = Assert.Single(delivery.Files);
        Assert.Equal("/tmp/a.png", file.FilePath);
        Assert.Equal("mm-target", file.Target);
        Assert.Equal("cron", file.Kind);
        // prompt 轮次自己已经落库，不再追加固定消息
        Assert.Empty(turns.Spoken);
    }

    [Fact]
    public async Task Push_IsSentWhenDeliverRequestsIt()
    {
        var (runner, jobs, _, _, pusher) = Create();
        var job = Job(Mode.Work, "message", "记得喝水", deliver: "local,push");
        await jobs.UpsertAsync(job);

        await runner.ExecuteAsync(job.Id, Scheduled);

        var push = Assert.Single(pusher.Sent);
        Assert.Equal(job.Name, push.Title);
        Assert.Equal("记得喝水", push.Body);
    }

    [Fact]
    public async Task ScriptFailure_IncrementsStreak_AndDisablesAtLimit()
    {
        var scripts = new FakeScriptRunner(new CronScriptResult(1, string.Empty, "boom"));
        var (runner, jobs, _, _, _) = Create(scripts: scripts, maxFailureStreak: 2);
        var job = Job(Mode.Work, "script", "exit 1") with { FailureStreak = 1 };
        await jobs.UpsertAsync(job);

        await runner.ExecuteAsync(job.Id, Scheduled);

        var updated = await jobs.GetAsync(job.Id);
        Assert.False(updated!.Enabled);
        Assert.Equal(2, updated.FailureStreak);
        Assert.Equal("failed", updated.LastStatus);
        Assert.Contains("boom", updated.LastError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AlreadyClaimedTrigger_IsNotRunAgain()
    {
        var (runner, jobs, turns, _, _) = Create();
        var job = Job(Mode.Work, "message", "只跑一次");
        await jobs.UpsertAsync(job);
        Assert.True(await jobs.TryClaimAsync(job.Id, Scheduled));

        await runner.ExecuteAsync(job.Id, Scheduled);

        var execution = Assert.Single(await jobs.ListExecutionsAsync(job.Id));
        Assert.Equal("running", execution.State);
        Assert.Empty(turns.Spoken);
    }

    private static (CronRunner Runner, FakeCronRepository Jobs, RecordingTurnService Turns, RecordingDelivery Delivery, FakePusher Pusher) Create(
        LaneRecord[]? lanes = null,
        ICronScriptRunner? scripts = null,
        int maxFailureStreak = 5)
    {
        var jobs = new FakeCronRepository();
        var turns = new RecordingTurnService();
        var delivery = new RecordingDelivery();
        var pusher = new FakePusher();
        var config = new WorkspaceConfig(
            Path.GetTempPath(),
            new ModelProfiles(
                new ModelEndpoint("http://localhost:1/v1", "main", "MAIN_KEY"),
                new ModelEndpoint("http://localhost:1/v1", "small", "SMALL_KEY")),
            new ChannelAccounts(),
            new ProactiveOptions(
                new HeartbeatOptions(Enabled: false),
                Cron: true,
                CronJobs: new CronOptions(MaxFailureStreak: maxFailureStreak)),
            new ToolOptions());
        var runner = new CronRunner(
            jobs,
            new FakeScheduler(),
            scripts ?? new FakeScriptRunner(new CronScriptResult(0, "ok", string.Empty)),
            new ThrowingSessionService(),
            turns,
            new FakeLaneRepository(lanes ?? []),
            delivery,
            pusher,
            config);
        return (runner, jobs, turns, delivery, pusher);
    }

    private static CronJobRecord Job(
        Mode mode,
        string kind,
        string payload,
        string deliver = "local",
        string? laneId = null)
        => new(
            "cron_test",
            "测试任务",
            "every:30m",
            kind,
            payload,
            deliver,
            Enabled: true,
            LastRunAt: null,
            CreatedAt: DateTimeOffset.UtcNow,
            Mode: mode,
            LaneId: laneId);

    private sealed class FakeScheduler : ICronScheduler
    {
        public List<string?> Refreshed { get; } = [];

        public string? Validate(string schedule) => CronScheduleParser.Validate(schedule);

        public Task RefreshAsync(string? jobId = null, CancellationToken cancellationToken = default)
        {
            Refreshed.Add(jobId);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeScriptRunner(CronScriptResult result) : ICronScriptRunner
    {
        public Task<CronScriptResult> RunAsync(string command, CancellationToken cancellationToken = default)
            => Task.FromResult(result);
    }

    private sealed class FakePusher : INotificationPusher
    {
        public List<(string Title, string Body)> Sent { get; } = [];

        public Task PushAsync(string title, string body, CancellationToken cancellationToken = default)
        {
            Sent.Add((title, body));
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingSessionService : ISessionService
    {
        public Task<PageResult<SessionSummary>> ListAsync(SessionListRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SessionDetail> GetAsync(string sessionId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SessionDetail> OpenAsync(string sessionId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SessionDetail> CreateAsync(SessionCreateRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task RenameAsync(string sessionId, string title, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task ArchiveAsync(string sessionId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task SetActiveForLaneAsync(string sessionId, string laneId, CancellationToken cancellationToken = default)
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
