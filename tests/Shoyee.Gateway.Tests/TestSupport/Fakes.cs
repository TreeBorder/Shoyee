using Xunit;
using Shoyee.Contracts.Abstractions;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Options;
using Shoyee.Contracts.Rpc;
using Shoyee.Storage.Abstractions;

namespace Shoyee.Gateway.Tests.TestSupport;

/// <summary>只记录「被怎么调用」的会话服务假实现。</summary>
internal sealed class FakeSessionService : ISessionService
{
    public string? LastLaneMessagesLaneId { get; private set; }

    public SessionCreateRequest? LastCreate { get; private set; }

    public Task<PageResult<SessionSummary>> ListAsync(
        SessionListRequest request,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new PageResult<SessionSummary>([
            new SessionSummary(
                "ses_1",
                Mode.Work,
                LaneChannel.Mattermost,
                "mattermost:work",
                "周报",
                false,
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch,
                3),
        ]));

    public Task<SessionDetail> GetAsync(string sessionId, CancellationToken cancellationToken = default)
        => Task.FromResult(Detail(sessionId));

    public Task<SessionDetail> OpenAsync(string sessionId, CancellationToken cancellationToken = default)
        => Task.FromResult(Detail(sessionId));

    public Task<SessionDetail> CreateAsync(
        SessionCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        LastCreate = request;
        return Task.FromResult(Detail("ses_new"));
    }

    public Task RenameAsync(string sessionId, string title, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task ArchiveAsync(string sessionId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task SetActiveForLaneAsync(
        string sessionId,
        string laneId,
        CancellationToken cancellationToken = default)
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
    {
        LastLaneMessagesLaneId = laneId;
        return Task.FromResult(new SessionMessagesResult([], false));
    }

    public string? LastSnapshotSessionId { get; private set; }

    public SessionExportRequest? LastExport { get; private set; }

    public Task<SessionSnapshot> GetSnapshotAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        LastSnapshotSessionId = sessionId;
        return Task.FromResult(new SessionSnapshot("冻结的提示词"));
    }

    public Task<SessionExportResult> ExportAsync(
        SessionExportRequest request,
        CancellationToken cancellationToken = default)
    {
        LastExport = request;
        return Task.FromResult(new SessionExportResult("/tmp/export.md"));
    }

    private static SessionDetail Detail(string sessionId) => new(
        sessionId,
        Mode.Work,
        LaneChannel.Mattermost,
        "mattermost:work",
        "会话",
        false,
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch,
        0,
        "mattermost:dm:test",
        null,
        false);
}

/// <summary>轮次服务假实现：只记录请求。</summary>
internal sealed class FakeTurnService : ITurnService
{
    public List<TurnSendRequest> Sent { get; } = [];

    public (string SessionId, string? TurnId)? LastCancel { get; private set; }

    public ApprovalRespondRequest? LastApproval { get; private set; }

    public Task<TurnRef> SendAsync(TurnSendRequest request, CancellationToken cancellationToken = default)
    {
        Sent.Add(request);
        return Task.FromResult(new TurnRef("trn_test"));
    }

    public Task<TurnOutcome> SendAndWaitAsync(
        TurnSendRequest request,
        CancellationToken cancellationToken = default)
    {
        Sent.Add(request);
        return Task.FromResult(new TurnOutcome("trn_test", true, "好了"));
    }

    public Task<TurnOutcome> AppendSpokenAsync(
        string laneId,
        string text,
        IReadOnlyList<MessageAttachment>? attachments = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new TurnOutcome("spk_test", true, text, null, attachments));

    public string? LastRetry { get; private set; }

    public Task<TurnRef> RetryAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        LastRetry = sessionId;
        return Task.FromResult(new TurnRef("trn_retry"));
    }

    public Task CancelAsync(string sessionId, string? turnId, CancellationToken cancellationToken = default)
    {
        LastCancel = (sessionId, turnId);
        return Task.CompletedTask;
    }

    public Task RespondApprovalAsync(
        ApprovalRespondRequest request,
        CancellationToken cancellationToken = default)
    {
        LastApproval = request;
        return Task.CompletedTask;
    }
}

/// <summary>语音合成假实现：记录请求，返回固定资产。</summary>
internal sealed class FakeTtsService : ITtsService
{
    public TtsSynthesizeRequest? LastRequest { get; private set; }

    public Task<TtsSynthesizeResult> SynthesizeAsync(
        TtsSynthesizeRequest request,
        CancellationToken cancellationToken = default)
    {
        LastRequest = request;
        return Task.FromResult(new TtsSynthesizeResult(
            "assets/audio/2026/10/a.wav",
            "/tmp/shoyee/assets/audio/2026/10/a.wav",
            "audio/wav",
            4096,
            1500));
    }
}

/// <summary>图片生成假实现：记录请求，返回固定资产。</summary>
internal sealed class FakeImageService : IImageService
{
    public ImageRenderRequest? LastRequest { get; private set; }

    public Task<ImageRenderResult> RenderAsync(
        ImageRenderRequest request,
        CancellationToken cancellationToken = default)
    {
        LastRequest = request;
        return Task.FromResult(new ImageRenderResult(
            "assets/images/2026/10/a.png",
            "/tmp/shoyee/assets/images/2026/10/a.png",
            "image/png",
            2048));
    }
}

/// <summary>资产登记假实现：只认一个 id。</summary>
internal sealed class FakeAssetStore : IAssetStore
{
    public AssetRef Register(string absolutePath)
        => new("assets/a.png", absolutePath, "image/png", 10);

    public AssetRef? Get(string assetId)
        => assetId == "assets/a.png"
            ? new AssetRef(assetId, "/tmp/shoyee/assets/a.png", "image/png", 10)
            : null;
}

internal sealed class FakeSearchRepository : ISearchRepository
{
    public SessionSearchFilter? LastFilter { get; private set; }

    public Task<IReadOnlyList<SearchHit>> SearchSessionsAsync(
        string query,
        SessionSearchFilter filter,
        CancellationToken cancellationToken = default)
    {
        LastFilter = filter;
        return Task.FromResult<IReadOnlyList<SearchHit>>([]);
    }
}

internal sealed class FakeIndexRepository : IIndexRepository
{
    private readonly Dictionary<string, IndexEntry> _entries = new(StringComparer.Ordinal);

    public Task<IReadOnlyList<IndexEntry>> ListAsync(
        string? scope = null,
        string? query = null,
        bool enabledOnly = false,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<IndexEntry>>(
            _entries.Values.Where(entry => !enabledOnly || entry.Enabled).ToList());

    public Task<IndexEntry> UpsertAsync(IndexEntry entry, CancellationToken cancellationToken = default)
    {
        var stored = entry with { Id = entry.Id == 0 ? _entries.Count + 1 : entry.Id };
        _entries[stored.Key] = stored;
        return Task.FromResult(stored);
    }

    public Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
        => Task.FromResult(_entries.Remove(key));
}

internal sealed class FakeLaneRepository : ILaneRepository
{
    private readonly Dictionary<string, LaneRecord> _lanes = new(StringComparer.Ordinal);

    public void Seed(params LaneRecord[] lanes)
    {
        foreach (var lane in lanes)
        {
            _lanes[lane.Id] = lane;
        }
    }

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
        => Task.FromResult<DateTimeOffset?>(null);
}

internal sealed class FakeStateRepository : IStateRepository
{
    private readonly Dictionary<string, StateRecord> _states = new(StringComparer.Ordinal)
    {
        ["mood"] = new StateRecord("mood", "心情", "平静", "说明", true, DateTimeOffset.UnixEpoch),
    };

    public Task<IReadOnlyList<StateRecord>> GetAllAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<StateRecord>>(_states.Values.ToList());

    public Task<StateRecord?> GetAsync(string key, CancellationToken cancellationToken = default)
        => Task.FromResult(_states.GetValueOrDefault(key));

    public Task<bool> SetAsync(
        string key,
        string value,
        bool byAgent = false,
        CancellationToken cancellationToken = default)
    {
        if (!_states.TryGetValue(key, out var record) || (byAgent && !record.AgentEditable))
        {
            return Task.FromResult(false);
        }

        _states[key] = record with { Value = value, UpdatedAt = DateTimeOffset.UtcNow };
        return Task.FromResult(true);
    }

    public Task<StateRecord> UpsertAsync(StateRecord record, CancellationToken cancellationToken = default)
    {
        _states[record.Key] = record;
        return Task.FromResult(record);
    }

    public Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
        => Task.FromResult(_states.Remove(key));

    public Task SeedIfEmptyAsync(
        IReadOnlyList<StateRecord> defaults,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

internal sealed class FakeUsageRepository : IUsageRepository
{
    public Task AddAsync(
        string? sessionId,
        string? turnId,
        string purpose,
        string model,
        int inputTokens,
        int outputTokens,
        int cacheHitTokens,
        int cacheMissTokens,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<UsageTotals> GetTotalsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        string? sessionId = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new UsageTotals(2, 100, 20, 80, 20));

    public Task<UsageCall?> GetLastCallAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<UsageCall?>(null);
}

/// <summary>内存 cron 仓库：任务定义 + 执行台账。</summary>
internal sealed class FakeCronRepository : ICronRepository
{
    private readonly Dictionary<string, CronJobRecord> _jobs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<CronExecutionRecord>> _runs = new(StringComparer.Ordinal);

    public void Seed(CronJobRecord job)
    {
        _jobs[job.Id] = job;
    }

    public void SeedExecution(CronExecutionRecord run)
    {
        if (!_runs.TryGetValue(run.JobId, out var list))
        {
            list = [];
            _runs[run.JobId] = list;
        }

        list.Add(run);
    }

    public Task<IReadOnlyList<CronJobRecord>> ListAsync(
        bool enabledOnly = true,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<CronJobRecord>>(
            _jobs.Values.Where(job => !enabledOnly || job.Enabled).ToList());

    public Task<CronJobRecord?> GetAsync(string jobId, CancellationToken cancellationToken = default)
        => Task.FromResult(_jobs.TryGetValue(jobId, out var job) ? job : null);

    public Task<CronJobRecord> UpsertAsync(CronJobRecord job, CancellationToken cancellationToken = default)
    {
        _jobs[job.Id] = job;
        return Task.FromResult(job);
    }

    public Task<bool> DeleteAsync(string jobId, CancellationToken cancellationToken = default)
    {
        _runs.Remove(jobId);
        return Task.FromResult(_jobs.Remove(jobId));
    }

    public Task<bool> SetEnabledAsync(
        string jobId,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        if (!_jobs.TryGetValue(jobId, out var job))
        {
            return Task.FromResult(false);
        }

        _jobs[jobId] = job with { Enabled = enabled };
        return Task.FromResult(true);
    }

    public Task SetNextRunAsync(
        string jobId,
        DateTimeOffset? nextRunAt,
        CancellationToken cancellationToken = default)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            _jobs[jobId] = job with { NextRunAt = nextRunAt };
        }

        return Task.CompletedTask;
    }

    public Task SetStateAsync(
        string jobId,
        string? lastStatus,
        string? lastError,
        int failureStreak,
        CancellationToken cancellationToken = default)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            _jobs[jobId] = job with { LastStatus = lastStatus, LastError = lastError, FailureStreak = failureStreak };
        }

        return Task.CompletedTask;
    }

    public Task<bool> TryClaimAsync(
        string jobId,
        DateTimeOffset scheduledFor,
        CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    public Task CompleteAsync(
        string jobId,
        DateTimeOffset scheduledFor,
        string state,
        string? result,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<CronExecutionRecord>> ListExecutionsAsync(
        string jobId,
        int limit = 10,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<CronExecutionRecord>>(
            _runs.TryGetValue(jobId, out var list)
                ? list.OrderByDescending(run => run.ScheduledFor).Take(limit).ToList()
                : []);
}

/// <summary>只记录「刷新了哪个任务」的调度器假实现。</summary>
internal sealed class FakeCronScheduler : ICronScheduler
{
    public List<string?> Refreshed { get; } = [];

    public int ValidateCalls { get; private set; }

    public string? Validate(string schedule)
    {
        ValidateCalls++;
        return null;
    }

    public Task RefreshAsync(string? jobId = null, CancellationToken cancellationToken = default)
    {
        Refreshed.Add(jobId);
        return Task.CompletedTask;
    }
}

/// <summary>只记录调用次数的配置读写假实现。</summary>
internal sealed class FakeConfigReloader : IConfigReloader
{
    public FakeConfigReloader(WorkspaceConfig current)
    {
        Current = current;
    }

    public WorkspaceConfig Current { get; }

    public int ReloadCalls { get; private set; }

    public ConfigReloadResult Result { get; set; } = new(["tools"], []);

    public int SetCalls { get; private set; }

    public ConfigSetRequest? LastSetRequest { get; private set; }

    public ConfigSetResult SetResult { get; set; } = new(["tools.deniedPaths"], [], "/tmp/config.json", []);

    public Task<ConfigReloadResult> ReloadAsync(CancellationToken cancellationToken = default)
    {
        ReloadCalls++;
        return Task.FromResult(Result);
    }

    public Task<ConfigSetResult> SetAsync(
        ConfigSetRequest request,
        CancellationToken cancellationToken = default)
    {
        SetCalls++;
        LastSetRequest = request;
        return Task.FromResult(SetResult);
    }
}
