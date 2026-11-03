using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Shoyee.Contracts.Abstractions;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Options;
using Shoyee.Storage.Entities.Channel;
using Shoyee.Storage.Entities.Session;
using Shoyee.Storage.Abstractions;
using Shoyee.Storage.Sqlite;
using Shoyee.Storage.Sqlite.Infrastructure;
using Xunit;

namespace Shoyee.Storage.Tests;

/// <summary>临时库 + DI 的测试夹具。</summary>
public sealed class StorageFixture : IAsyncLifetime
{
    internal const string WorkLane = "mattermost:work";
    internal const string WorkSession = "ses_work";

    /// <summary>工作区临时目录。</summary>
    public string Root { get; private set; } = string.Empty;

    /// <summary>服务容器。</summary>
    public ServiceProvider Provider { get; private set; } = null!;

    /// <summary>上下文工厂。</summary>
    public IDbContextFactory<ShoyeeDbContext> Factory { get; private set; } = null!;

    /// <summary>消息投影仓库。</summary>
    public IMessageRepository Messages { get; private set; } = null!;

    /// <summary>轮次仓库（RP 真源）。</summary>
    public IRoundRepository Rounds { get; private set; } = null!;

    /// <summary>检索。</summary>
    public ISearchRepository Search { get; private set; } = null!;

    /// <summary>INDEX。</summary>
    public IIndexRepository Index { get; private set; } = null!;

    /// <summary>通用状态仓库。</summary>
    public IStateRepository States { get; private set; } = null!;

    /// <summary>Lane 仓库。</summary>
    public ILaneRepository Lanes { get; private set; } = null!;

    /// <summary>会话仓库。</summary>
    public ISessionRepository Sessions { get; private set; } = null!;

    /// <summary>摘要仓库。</summary>
    public ISummaryRepository Summaries { get; private set; } = null!;

    /// <summary>用量仓库。</summary>
    public IUsageRepository Usage { get; private set; } = null!;

    /// <summary>cron 仓库。</summary>
    public ICronRepository Cron { get; private set; } = null!;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        Root = Path.Combine(Path.GetTempPath(), "shoyee-storage-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddShoyeeSqliteStorage(BuildConfig(Root));
        Provider = services.BuildServiceProvider();

        await Provider.GetRequiredService<IStorageInitializer>().InitializeAsync();

        Factory = Provider.GetRequiredService<IDbContextFactory<ShoyeeDbContext>>();
        Messages = Provider.GetRequiredService<IMessageRepository>();
        Rounds = Provider.GetRequiredService<IRoundRepository>();
        Search = Provider.GetRequiredService<ISearchRepository>();
        Index = Provider.GetRequiredService<IIndexRepository>();
        States = Provider.GetRequiredService<IStateRepository>();
        Lanes = Provider.GetRequiredService<ILaneRepository>();
        Sessions = Provider.GetRequiredService<ISessionRepository>();
        Summaries = Provider.GetRequiredService<ISummaryRepository>();
        Usage = Provider.GetRequiredService<IUsageRepository>();
        Cron = Provider.GetRequiredService<ICronRepository>();

        await using var db = await Factory.CreateDbContextAsync();
        var now = DateTimeOffset.UtcNow;
        db.Lanes.AddRange(
            new LaneEntity
            {
                Id = WorkLane,
                Channel = LaneChannel.Mattermost,
                Mode = Mode.Work,
                ExternalKey = "mm-work",
                CreatedAt = now,
            },
            new LaneEntity
            {
                Id = "desktop:roleplay",
                Channel = LaneChannel.Desktop,
                Mode = Mode.Roleplay,
                ExternalKey = "desktop",
                CreatedAt = now,
            });
        db.Sessions.Add(new SessionEntity
        {
            Id = WorkSession,
            Mode = Mode.Work,
            Origin = LaneChannel.Mattermost,
            LaneId = WorkLane,
            SessionKey = "mattermost:dm:abc",
            Title = "工作会话",
            CreatedAt = now,
            LastActiveAt = now,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>为单个测试建一条独立 Lane（避免用例之间互相污染）。</summary>
    public async Task<string> CreateLaneAsync(Mode mode = Mode.Roleplay, LaneChannel channel = LaneChannel.Desktop)
    {
        var laneId = $"{channel.ToString().ToLowerInvariant()}:{mode.ToString().ToLowerInvariant()}:{Guid.NewGuid():N}";
        await using var db = await Factory.CreateDbContextAsync();
        db.Lanes.Add(new LaneEntity
        {
            Id = laneId,
            Channel = channel,
            Mode = mode,
            ExternalKey = laneId,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        return laneId;
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        await Provider.DisposeAsync();
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // 临时目录清理失败不影响测试结论
        }
    }

    private static WorkspaceConfig BuildConfig(string root)
    {
        var endpoint = new ModelEndpoint("http://127.0.0.1:1", "test-model", "SHOYEE_TEST_KEY");
        return new WorkspaceConfig(
            Root: root,
            Models: new ModelProfiles(endpoint, endpoint),
            Channels: new ChannelAccounts(),
            Proactive: new ProactiveOptions(new HeartbeatOptions()),
            Tools: new ToolOptions());
    }
}

/// <summary>轮次落盘 + 投影 + 检索 + INDEX 的集成测试。</summary>
/// <remarks>
/// 每个用例一份全新库：轮次号是 RP 全局序号、摘要也是全局一份，共享夹具会让用例互相污染。
/// </remarks>
public sealed class StorageIntegrationTests : IAsyncLifetime
{
    private StorageFixture _fixture = null!;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        _fixture = new StorageFixture();
        await _fixture.InitializeAsync();
    }

    /// <inheritdoc />
    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task 一轮落盘_请求与响应完整还原且带工具调用()
    {
        var lane = await _fixture.CreateLaneAsync();
        var request = new[] { new ChatMessage(ChatRole.User, "帮我看看工作区") };
        var response = new[]
        {
            new ChatMessage(ChatRole.Assistant, [
                new TextContent("我先查一下"),
                new FunctionCallContent("call_1", "file_access_read", new Dictionary<string, object?> { ["fileName"] = "a.md" }),
            ]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call_1", "内容")]),
            new ChatMessage(ChatRole.Assistant, "工作区里有 a.md"),
        };

        var round = await _fixture.Rounds.AppendAsync(lane, request, response);
        var recent = await _fixture.Rounds.GetRecentAsync();

        var only = Assert.Single(recent);
        Assert.Equal(round.RoundNo, only.RoundNo);
        Assert.Single(only.Request);
        Assert.Equal(3, only.Response.Count);
        Assert.Single(only.Response[0].Contents.OfType<FunctionCallContent>());
        Assert.Equal("工作区里有 a.md", only.Response[2].Text);
    }

    [Fact]
    public async Task 轮次号单调递增_并保留全部历史()
    {
        var lane = await _fixture.CreateLaneAsync();
        for (var i = 0; i < 3; i++)
        {
            await _fixture.Rounds.AppendAsync(
                lane,
                [new ChatMessage(ChatRole.User, $"第{i}轮")],
                [new ChatMessage(ChatRole.Assistant, $"第{i}轮回复")]);
        }

        var recent = await _fixture.Rounds.GetRecentAsync(count: 2);

        Assert.Equal(3, await _fixture.Rounds.GetMaxRoundNoAsync());
        Assert.Equal(3, await _fixture.Rounds.CountAsync());
        Assert.Equal([2, 3], recent.Select(r => r.RoundNo));
    }

    [Fact]
    public async Task 跨Lane_共用一条全局时间线()
    {
        var desktop = await _fixture.CreateLaneAsync();
        var phone = await _fixture.CreateLaneAsync(Mode.Roleplay, LaneChannel.Mattermost);

        await _fixture.Rounds.AppendAsync(
            desktop,
            [new ChatMessage(ChatRole.User, "在电脑上")],
            [new ChatMessage(ChatRole.Assistant, "嗯")]);
        await _fixture.Rounds.AppendAsync(
            phone,
            [new ChatMessage(ChatRole.User, "出门了")],
            [new ChatMessage(ChatRole.Assistant, "路上小心")]);
        await _fixture.Rounds.AppendAsync(
            desktop,
            [new ChatMessage(ChatRole.User, "回来了")],
            [new ChatMessage(ChatRole.Assistant, "欢迎回来")]);

        var recent = await _fixture.Rounds.GetRecentAsync();

        Assert.Equal([1, 2, 3], recent.Select(r => r.RoundNo).ToArray());
        Assert.Equal([desktop, phone, desktop], recent.Select(r => r.LaneId).ToArray());
    }

    [Fact]
    public async Task 两字中文检索_走LIKE兜底()
    {
        var lane = await _fixture.CreateLaneAsync();
        await _fixture.Rounds.AppendAsync(
            lane,
            [new ChatMessage(ChatRole.User, "今天打卡完成，记得明天继续")],
            [new ChatMessage(ChatRole.Assistant, "记住了")]);

        var hits = await _fixture.Search.SearchSessionsAsync("打卡", new SessionSearchFilter(LaneId: lane));

        var hit = Assert.Single(hits);
        Assert.Equal(lane, hit.LaneId);
        Assert.Null(hit.SessionId);
        Assert.Contains("打卡", hit.Snippet);
    }

    [Fact]
    public async Task 三字中文检索_命中FTS()
    {
        var lane = await _fixture.CreateLaneAsync();
        await _fixture.Rounds.AppendAsync(
            lane,
            [new ChatMessage(ChatRole.User, "今天的打卡记录已整理好")],
            [new ChatMessage(ChatRole.Assistant, "收到")]);

        var hits = await _fixture.Search.SearchSessionsAsync("打卡记", new SessionSearchFilter(LaneId: lane));

        Assert.Contains(hits, h => h.LaneId == lane);
    }

    [Fact]
    public async Task 带附件的消息_投影保存附件与展示正文()
    {
        var attachments = new MessageAttachment[]
        {
            new("/tmp/inbox/a.png", "a.png", "image/png", 10, AttachmentKind.Image),
        };
        var message = new ChatMessage(ChatRole.User, "[附件] /tmp/inbox/a.png (image/png)\n看图")
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [ChatMessageMarkers.DisplayText] = "看图",
                [ChatMessageMarkers.Attachments] = attachments,
            },
        };

        await _fixture.Messages.AppendAsync(
            StorageFixture.WorkLane,
            StorageFixture.WorkSession,
            roundNo: null,
            message);

        var row = Assert.Single(await _fixture.Messages.GetPageAsync(sessionId: StorageFixture.WorkSession));
        Assert.Equal("看图", row.Content);
        Assert.Equal("/tmp/inbox/a.png", Assert.Single(row.Attachments!).Path);
    }

    [Fact]
    public async Task 异步结果可回写到工具结果且按路径去重()
    {
        var row = await _fixture.Messages.AppendAsync(
            StorageFixture.WorkLane,
            StorageFixture.WorkSession,
            roundNo: null,
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call_cam", "已拍摄并发送照片（任务 task_abc）")]));

        Assert.NotNull(row);
        var attachment = new MessageAttachment("/tmp/photo.png", "photo.png", "image/png", 10, AttachmentKind.Image);

        var updated = await _fixture.Messages.AttachToToolResultAsync(
            StorageFixture.WorkLane,
            "task_abc",
            [attachment]);

        Assert.NotNull(updated);
        Assert.Equal("/tmp/photo.png", Assert.Single(updated!.Attachments!).Path);

        // 重复回写（事件重放 / 桌面重连）不能出现第二份
        var again = await _fixture.Messages.AttachToToolResultAsync(
            StorageFixture.WorkLane,
            "task_abc",
            [attachment]);

        Assert.NotNull(again);
        Assert.Single(again!.Attachments!);
    }

    [Fact]
    public async Task 工作模式投影_可按会话或Lane查且带模式()
    {
        await _fixture.Messages.AppendAsync(
            StorageFixture.WorkLane,
            StorageFixture.WorkSession,
            roundNo: null,
            new ChatMessage(ChatRole.User, "把周报按项目拆一下"));

        var bySession = await _fixture.Messages.GetPageAsync(sessionId: StorageFixture.WorkSession);
        var hits = await _fixture.Search.SearchSessionsAsync("周报", new SessionSearchFilter(Mode: Mode.Work));

        Assert.Contains(bySession, m => m.Content.Contains("周报"));
        Assert.All(bySession, m => Assert.Null(m.RoundNo));
        Assert.NotEmpty(hits);
        Assert.All(hits, h => Assert.Equal(Mode.Work, h.Mode));
    }

    [Fact]
    public async Task 工具结果不落正文_所以搜不到()
    {
        var lane = await _fixture.CreateLaneAsync();
        await _fixture.Rounds.AppendAsync(
            lane,
            [new ChatMessage(ChatRole.User, "查一下")],
            [new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call_x", "内部实现细节关键字")])]);

        var hits = await _fixture.Search.SearchSessionsAsync("内部实现细节关键字", new SessionSearchFilter(LaneId: lane));

        Assert.Empty(hits);
    }

    [Fact]
    public async Task 投影分页按id升序()
    {
        var lane = await _fixture.CreateLaneAsync();
        for (var i = 0; i < 5; i++)
        {
            await _fixture.Messages.AppendAsync(
                lane,
                sessionId: null,
                roundNo: i + 1,
                new ChatMessage(ChatRole.User, $"第{i}条"));
        }

        var page = await _fixture.Messages.GetPageAsync(laneId: lane, limit: 3);

        Assert.Equal(3, page.Count);
        Assert.True(page[0].Id < page[1].Id && page[1].Id < page[2].Id);
    }

    [Fact]
    public async Task 通用状态_默认五条_新增不需要改代码()
    {
        var states = await _fixture.States.GetAllAsync();

        Assert.Contains(states, x => x.Key == "mood");
        Assert.Contains(states, x => x.Key == "longing");
        Assert.Contains(states, x => x.Key == "scene");
        Assert.Contains(states, x => x.Key == "outfit");
        Assert.Equal(StateDefaults.All.Count, states.Count);

        var added = await _fixture.States.UpsertAsync(new StateRecord(
            "focus", "专注度", "0.5", "0–1，工作时参考", true, default));

        Assert.Equal("focus", added.Key);
        Assert.Equal(StateDefaults.All.Count + 1, (await _fixture.States.GetAllAsync()).Count);
    }

    [Fact]
    public async Task 通用状态_她不能改非AgentEditable的状态()
    {
        Assert.False(await _fixture.States.SetAsync("longing", "0.9", byAgent: true));
        Assert.True(await _fixture.States.SetAsync("mood", "有点累", byAgent: true));

        var mood = await _fixture.States.GetAsync("mood");
        var longing = await _fixture.States.GetAsync("longing");

        Assert.Equal("有点累", mood!.Value);
        Assert.NotEqual("0.9", longing!.Value);
    }

    [Fact]
    public async Task 会话指针_生效_归档后回落()
    {
        var lane = await _fixture.CreateLaneAsync(Mode.Work, LaneChannel.Mattermost);
        var now = DateTimeOffset.UtcNow;
        var session = await _fixture.Sessions.CreateAsync(new SessionRecord(
            "ses_" + Guid.NewGuid().ToString("N"), Mode.Work, LaneChannel.Mattermost, lane,
            "mattermost:dm:x", "测试会话", null, now, now, false));

        await _fixture.Sessions.SetActiveForLaneAsync(lane, session.Id);
        var active = await _fixture.Sessions.FindActiveAsync(lane);

        Assert.Equal(session.Id, active!.Id);

        await _fixture.Sessions.ArchiveAsync(session.Id);
        Assert.Null(await _fixture.Sessions.FindActiveAsync(lane));
    }

    [Fact]
    public async Task 摘要_追加后取最新()
    {
        await _fixture.Summaries.AppendAsync(throughRound: 30, roundCount: 30, content: "第一段摘要");
        await _fixture.Summaries.AppendAsync(throughRound: 60, roundCount: 30, content: "第一段摘要\n第二段摘要");

        var latest = await _fixture.Summaries.GetLatestAsync();

        Assert.NotNull(latest);
        Assert.Equal(60, latest!.ThroughRound);
        Assert.Contains("第二段摘要", latest.Content);
    }

    [Fact]
    public async Task 摘要_全局只有一份_取最新即可()
    {
        await _fixture.Summaries.AppendAsync(throughRound: 30, roundCount: 30, content: "第一段");
        await _fixture.Summaries.AppendAsync(throughRound: 60, roundCount: 30, content: "第二段");

        var latest = await _fixture.Summaries.GetLatestAsync();

        Assert.NotNull(latest);
        Assert.Equal(60, latest!.ThroughRound);
        Assert.Equal("第二段", latest.Content);
    }

    [Fact]
    public async Task 用量_按区间汇总()
    {
        await _fixture.Usage.AddAsync(null, null, "chat", "test-model", 100, 20, 80, 20);
        await _fixture.Usage.AddAsync(null, null, "summary", "test-model", 50, 10, 0, 50);

        var totals = await _fixture.Usage.GetTotalsAsync(
            DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(5));

        Assert.Equal(2, totals.Calls);
        Assert.Equal(150, totals.InputTokens);
        Assert.Equal(30, totals.OutputTokens);
        Assert.Equal(80, totals.CacheHitTokens);
    }

    [Fact]
    public async Task cron_同一触发只认领一次()
    {
        var scheduled = DateTimeOffset.UtcNow;
        Assert.True(await _fixture.Cron.TryClaimAsync("job_1", scheduled));
        Assert.False(await _fixture.Cron.TryClaimAsync("job_1", scheduled));

        await _fixture.Cron.CompleteAsync("job_1", scheduled, "succeeded", "done");
        Assert.True(await _fixture.Cron.TryClaimAsync("job_1", scheduled.AddMinutes(1)));
    }

    [Fact]
    public async Task cron_任务定义与执行台账可往返()
    {
        var job = new CronJobRecord(
            "cron_1",
            "每日汇报",
            "every:30m",
            "prompt",
            "整理今天的消息",
            "local,push",
            Enabled: true,
            LastRunAt: null,
            CreatedAt: DateTimeOffset.UtcNow,
            Mode: Mode.Roleplay,
            LaneId: "desktop:roleplay",
            NextRunAt: DateTimeOffset.UtcNow.AddMinutes(30));

        await _fixture.Cron.UpsertAsync(job);
        var loaded = await _fixture.Cron.GetAsync("cron_1");

        Assert.NotNull(loaded);
        Assert.Equal(Mode.Roleplay, loaded!.Mode);
        Assert.Equal("desktop:roleplay", loaded.LaneId);
        Assert.Equal("local,push", loaded.Deliver);
        Assert.Equal(job.NextRunAt, loaded.NextRunAt);

        var scheduled = DateTimeOffset.UtcNow;
        Assert.True(await _fixture.Cron.TryClaimAsync("cron_1", scheduled));
        await _fixture.Cron.CompleteAsync("cron_1", scheduled, "succeeded", "产出正文");
        var execution = Assert.Single(await _fixture.Cron.ListExecutionsAsync("cron_1"));
        Assert.Equal("succeeded", execution.State);
        Assert.Equal("产出正文", execution.Result);

        await _fixture.Cron.SetStateAsync("cron_1", "succeeded", null, 0);
        await _fixture.Cron.SetNextRunAsync("cron_1", null);
        var updated = await _fixture.Cron.GetAsync("cron_1");
        Assert.Equal("succeeded", updated!.LastStatus);
        Assert.Null(updated.NextRunAt);

        Assert.True(await _fixture.Cron.DeleteAsync("cron_1"));
        Assert.Null(await _fixture.Cron.GetAsync("cron_1"));
        Assert.Empty(await _fixture.Cron.ListExecutionsAsync("cron_1"));
    }

    [Fact]
    public async Task INDEX_按key覆盖且可删除()
    {
        var created = await _fixture.Index.UpsertAsync(new IndexEntry(
            0, "nas-photos", "家庭照片库", "path", "/tmp/photos", "all",
            ["照片"], "按年份分目录", false, default, default));

        var updated = await _fixture.Index.UpsertAsync(created with { Title = "照片库（新）" });
        var list = await _fixture.Index.ListAsync();

        var only = Assert.Single(list);
        Assert.Equal("照片库（新）", only.Title);
        Assert.Single(only.Tags!);

        Assert.True(await _fixture.Index.RemoveAsync("nas-photos"));
        Assert.False(await _fixture.Index.RemoveAsync("nas-photos"));
        Assert.Equal("nas-photos", updated.Key);
    }

    [Fact]
    public async Task 消息投影_保留思考与工具明细()
    {
        var lane = await _fixture.CreateLaneAsync(Mode.Work, LaneChannel.Mattermost);
        var session = "ses_" + Guid.NewGuid().ToString("N");
        var messages = new ChatMessage[]
        {
            new(ChatRole.Assistant, [new TextReasoningContent("先想一下"), new TextContent("好的")]),
            new(ChatRole.Assistant, [new FunctionCallContent(
                "call_1",
                "terminal",
                new Dictionary<string, object?> { ["command"] = "ls -la" })]),
            new(ChatRole.Tool, [new FunctionResultContent("call_1", "file-a\nfile-b")]),
        };

        await _fixture.Messages.AppendRangeAsync(lane, session, messages, 0);
        var page = await _fixture.Messages.GetPageAsync(sessionId: session, limit: 10);

        Assert.Equal(3, page.Count);
        Assert.Equal("先想一下", page[0].Thinking);
        Assert.Equal("call_1", page[1].ToolCallId);
        Assert.Contains("ls -la", page[1].ToolArguments);
        Assert.Equal("call_1", page[2].ToolCallId);
        Assert.Equal("file-a\nfile-b", page[2].ToolResult);
    }

    [Fact]
    public async Task INDEX_停用后不出现在启用列表里()
    {
        await _fixture.Index.UpsertAsync(new IndexEntry(
            0, "keep", "保留", "path", "/tmp/keep", "all", null, null, false, default, default));
        await _fixture.Index.UpsertAsync(new IndexEntry(
            0, "off", "停用", "path", "/tmp/off", "all", null, null, false, default, default, Enabled: false));

        var all = await _fixture.Index.ListAsync();
        var enabled = await _fixture.Index.ListAsync(enabledOnly: true);

        Assert.Equal(2, all.Count);
        Assert.False(all.Single(x => x.Key == "off").Enabled);
        var only = Assert.Single(enabled);
        Assert.Equal("keep", only.Key);
    }

    [Fact]
    public async Task 工作转录_批量追加后可增量续抄()
    {
        var lane = await _fixture.CreateLaneAsync(Mode.Work, LaneChannel.Mattermost);
        var session = "ses_" + Guid.NewGuid().ToString("N");

        await _fixture.Messages.AppendRangeAsync(
            lane,
            session,
            [
                new ChatMessage(ChatRole.System, "工作人设"),
                new ChatMessage(ChatRole.User, "把周报按项目拆一下"),
                new ChatMessage(ChatRole.Assistant, "好的"),
            ],
            startIndex: 0);

        Assert.Equal(3, await _fixture.Messages.GetProjectedIndexAsync(session));

        var added = await _fixture.Messages.AppendRangeAsync(
            lane,
            session,
            [
                new ChatMessage(ChatRole.User, "再补一条备注"),
                new ChatMessage(ChatRole.Assistant, "已补"),
            ],
            startIndex: 3);

        Assert.Equal(2, added.Count);
        Assert.Equal(5, await _fixture.Messages.GetProjectedIndexAsync(session));
        var page = await _fixture.Messages.GetPageAsync(sessionId: session);
        Assert.Equal(5, page.Count);
        Assert.All(page, m => Assert.Null(m.RoundNo));

        var last = await _fixture.Messages.GetLastAssistantAsync(sessionId: session);
        Assert.NotNull(last);
        Assert.Equal("已补", last!.Content);
    }

    [Fact]
    public async Task 工作转录_删除会话投影时同步清理检索索引()
    {
        var lane = await _fixture.CreateLaneAsync(Mode.Work, LaneChannel.Mattermost);
        var session = "ses_" + Guid.NewGuid().ToString("N");
        await _fixture.Messages.AppendRangeAsync(
            lane,
            session,
            [new ChatMessage(ChatRole.User, "独有词条海燕计划开工")],
            startIndex: 0);

        var before = await _fixture.Search.SearchSessionsAsync("海燕计划", new SessionSearchFilter(Mode: Mode.Work));
        Assert.Contains(before, h => h.SessionId == session);

        var deleted = await _fixture.Messages.DeleteSessionAsync(session);

        Assert.Equal(1, deleted);
        Assert.Equal(0, await _fixture.Messages.GetProjectedIndexAsync(session));
        var after = await _fixture.Search.SearchSessionsAsync("海燕计划", new SessionSearchFilter(Mode: Mode.Work));
        Assert.DoesNotContain(after, h => h.SessionId == session);
    }

    [Fact]
    public async Task 工作转录_同会话同下标不会抄出第二行()
    {
        var lane = await _fixture.CreateLaneAsync(Mode.Work, LaneChannel.Mattermost);
        var session = "ses_" + Guid.NewGuid().ToString("N");
        var batch = new ChatMessage[] { new(ChatRole.User, "只抄一次") };

        await _fixture.Messages.AppendRangeAsync(lane, session, batch, startIndex: 0);
        var duplicate = async () => await _fixture.Messages.AppendRangeAsync(lane, session, batch, startIndex: 0);

        await Assert.ThrowsAsync<DbUpdateException>(duplicate);
        Assert.Equal(1, await _fixture.Messages.CountAsync(sessionId: session));
    }

    [Fact]
    public async Task 会话分页_按最后活跃倒序且游标不重不漏()
    {
        var lane = await _fixture.CreateLaneAsync(Mode.Work, LaneChannel.Mattermost);
        var tag = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        var ids = new List<string>();
        foreach (var (suffix, minutesAgo) in new[] { ("old", 3), ("mid", 2), ("new", 1) })
        {
            var id = $"ses_{tag}_{suffix}";
            ids.Add(id);
            await _fixture.Sessions.CreateAsync(new SessionRecord(
                id, Mode.Work, LaneChannel.Mattermost, lane,
                $"mattermost:dm:{suffix}", suffix, null,
                now.AddMinutes(-minutesAgo), now.AddMinutes(-minutesAgo), false));
        }

        var first = await _fixture.Sessions.ListPageAsync(Mode.Work, lane, false, null, null, 2);
        Assert.Equal([ids[2], ids[1]], first.Select(s => s.Id));

        var second = await _fixture.Sessions.ListPageAsync(
            Mode.Work, lane, false, first[^1].LastActiveAt, first[^1].Id, 2);
        Assert.Equal([ids[0]], second.Select(s => s.Id));
    }
}
