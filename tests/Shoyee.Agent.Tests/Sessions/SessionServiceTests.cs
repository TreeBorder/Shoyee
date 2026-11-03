using Microsoft.Extensions.AI;
using Shoyee.Agent.Sessions;
using Shoyee.Agent.Tests.TestSupport;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Rpc;
using Shoyee.Storage.Abstractions;
using Xunit;

namespace Shoyee.Agent.Tests.Sessions;

/// <summary>工作会话领域服务：创建 / 打开 / 列表 / 改名归档 / 消息分页。</summary>
public sealed class SessionServiceTests
{
    private const string WorkLane = "mattermost:work";

    private static AgentTestBed CreateBed()
    {
        var bed = new AgentTestBed(new RecordingChatClient());
        bed.Lanes.Seed(new LaneRecord(
            WorkLane,
            LaneChannel.Mattermost,
            Mode.Work,
            "mm-work",
            "工作",
            null,
            null,
            null,
            DateTimeOffset.UtcNow));
        return bed;
    }

    [Fact]
    public async Task 创建工作会话_拼快照并存下状态()
    {
        using var bed = CreateBed();

        var detail = await bed.SessionService.CreateAsync(new SessionCreateRequest(Mode.Work, WorkLane, "周报整理"));

        Assert.Equal(Mode.Work, detail.Mode);
        Assert.Equal(WorkLane, detail.LaneId);
        Assert.Equal("周报整理", detail.Title);
        Assert.Equal(0, detail.MessageCount);
        Assert.True(detail.IsActiveForLane);
        Assert.True(bed.States.Rows.ContainsKey(detail.Id));

        // 快照里应该是拼好的工作模式提示词，而不是空串
        Assert.Contains("共用行为守则", bed.States.Rows[detail.Id], StringComparison.Ordinal);

        // 状态已落盘 → 踢出缓存后还能 resume
        bed.Cache.Evict(detail.Id);
        var opened = await bed.SessionService.OpenAsync(detail.Id);
        Assert.Equal(detail.Id, opened.Id);
    }

    [Fact]
    public async Task 快照_取建会话时冻结的提示词_不受后续文件修改影响()
    {
        using var bed = CreateBed();
        var soul = Path.Combine(bed.Root, "SOUL.md");
        File.AppendAllText(soul, "\n冻结标记甲\n");

        var detail = await bed.SessionService.CreateAsync(new SessionCreateRequest(Mode.Work, WorkLane));

        File.AppendAllText(soul, "\n冻结标记乙\n");
        var snapshot = await bed.SessionService.GetSnapshotAsync(detail.Id);

        Assert.Contains("冻结标记甲", snapshot.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("冻结标记乙", snapshot.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 快照_状态缺失_报state_missing()
    {
        using var bed = CreateBed();
        var now = DateTimeOffset.UtcNow;
        bed.Sessions.Seed(new SessionRecord(
            "ses_orphan", Mode.Work, LaneChannel.Mattermost, WorkLane,
            "mattermost:dm:orphan", "孤儿会话", null, now, now, Archived: false));

        var error = await Assert.ThrowsAsync<RpcException>(
            () => bed.SessionService.GetSnapshotAsync("ses_orphan"));

        Assert.Equal(RpcErrors.Internal, error.Code);
        Assert.Contains("state_missing", error.ErrorData!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task 导出Markdown_落到工作区exports并包含正文()
    {
        using var bed = CreateBed();
        var detail = await bed.SessionService.CreateAsync(new SessionCreateRequest(Mode.Work, WorkLane, "周报整理"));
        await bed.Messages.AppendRangeAsync(
            WorkLane, detail.Id,
            [new ChatMessage(ChatRole.User, "把周报拆一下"), new ChatMessage(ChatRole.Assistant, "拆成三块了")],
            startIndex: 0);

        var result = await bed.SessionService.ExportAsync(new SessionExportRequest(detail.Id));

        Assert.True(File.Exists(result.Path));
        Assert.StartsWith(Path.Combine(bed.Root, "exports"), result.Path, StringComparison.Ordinal);
        var content = await File.ReadAllTextAsync(result.Path);
        Assert.Contains("# 周报整理", content, StringComparison.Ordinal);
        Assert.Contains("拆成三块了", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 导出Json_结构可解析且含消息()
    {
        using var bed = CreateBed();
        var detail = await bed.SessionService.CreateAsync(new SessionCreateRequest(Mode.Work, WorkLane, "乙"));
        await bed.Messages.AppendRangeAsync(
            WorkLane, detail.Id,
            [new ChatMessage(ChatRole.User, "你好")],
            startIndex: 0);

        var result = await bed.SessionService.ExportAsync(new SessionExportRequest(detail.Id, "json"));

        Assert.EndsWith(".json", result.Path, StringComparison.Ordinal);
        using var document = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(result.Path));
        Assert.Equal(detail.Id, document.RootElement.GetProperty("session").GetProperty("id").GetString());
        Assert.Equal("你好", document.RootElement.GetProperty("messages")[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task 导出_不支持的格式_报invalid_params()
    {
        using var bed = CreateBed();
        var detail = await bed.SessionService.CreateAsync(new SessionCreateRequest(Mode.Work, WorkLane));

        var error = await Assert.ThrowsAsync<RpcException>(
            () => bed.SessionService.ExportAsync(new SessionExportRequest(detail.Id, "pdf")));

        Assert.Equal(RpcErrors.InvalidParams, error.Code);
    }

    [Fact]
    public async Task 创建RP会话_被拒绝()
    {
        using var bed = CreateBed();

        var error = await Assert.ThrowsAsync<RpcException>(
            () => bed.SessionService.CreateAsync(new SessionCreateRequest(Mode.Roleplay)));

        Assert.Equal(RpcErrors.InvalidParams, error.Code);
    }

    [Fact]
    public async Task 打开已归档会话_报archived()
    {
        using var bed = CreateBed();
        var detail = await bed.SessionService.CreateAsync(new SessionCreateRequest(Mode.Work, WorkLane));
        await bed.SessionService.ArchiveAsync(detail.Id);

        var error = await Assert.ThrowsAsync<RpcException>(() => bed.SessionService.OpenAsync(detail.Id));

        Assert.Equal(RpcErrors.SessionArchived, error.Code);
    }

    [Fact]
    public async Task 打开状态缺失的会话_报state_missing()
    {
        using var bed = CreateBed();
        var now = DateTimeOffset.UtcNow;
        bed.Sessions.Seed(new SessionRecord(
            "ses_orphan", Mode.Work, LaneChannel.Mattermost, WorkLane,
            "mattermost:dm:orphan", "孤儿会话", null, now, now, Archived: false));

        var error = await Assert.ThrowsAsync<RpcException>(() => bed.SessionService.OpenAsync("ses_orphan"));

        Assert.Equal(RpcErrors.Internal, error.Code);
        Assert.Contains("state_missing", error.ErrorData!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task 列表_消息条数取投影真值且可翻页()
    {
        using var bed = CreateBed();
        var first = await bed.SessionService.CreateAsync(new SessionCreateRequest(Mode.Work, WorkLane, "甲"));
        var second = await bed.SessionService.CreateAsync(new SessionCreateRequest(Mode.Work, WorkLane, "乙"));
        var third = await bed.SessionService.CreateAsync(new SessionCreateRequest(Mode.Work, WorkLane, "丙"));

        await bed.Messages.AppendRangeAsync(
            WorkLane, first.Id,
            [new ChatMessage(ChatRole.User, "一"), new ChatMessage(ChatRole.Assistant, "二")],
            startIndex: 0);

        var page1 = await bed.SessionService.ListAsync(new SessionListRequest(Mode.Work, WorkLane, Limit: 2));
        Assert.Equal(2, page1.Items.Count);
        Assert.NotNull(page1.NextCursor);

        var page2 = await bed.SessionService.ListAsync(
            new SessionListRequest(Mode.Work, WorkLane, Limit: 2, Cursor: page1.NextCursor));
        Assert.Single(page2.Items);
        Assert.Null(page2.NextCursor);

        var all = page1.Items.Concat(page2.Items).ToList();
        Assert.Equal(3, all.Count);
        Assert.Equal(3, all.Select(s => s.Id).Distinct().Count());
        Assert.Equal(2, all.Single(s => s.Id == first.Id).MessageCount);

        // 最新的排在最前
        Assert.Equal(third.Id, all[0].Id);
        Assert.Contains(all, s => s.Id == second.Id);
    }

    [Fact]
    public async Task 改名与归档_归档后默认列表不返回()
    {
        using var bed = CreateBed();
        var detail = await bed.SessionService.CreateAsync(new SessionCreateRequest(Mode.Work, WorkLane, "旧名字"));

        await bed.SessionService.RenameAsync(detail.Id, "新名字");
        await bed.SessionService.ArchiveAsync(detail.Id);

        var renamed = await bed.SessionService.GetAsync(detail.Id);
        Assert.Equal("新名字", renamed.Title);
        Assert.True(renamed.Archived);

        var visible = await bed.SessionService.ListAsync(new SessionListRequest(Mode.Work));
        Assert.DoesNotContain(visible.Items, s => s.Id == detail.Id);

        var withArchived = await bed.SessionService.ListAsync(
            new SessionListRequest(Mode.Work, IncludeArchived: true));
        Assert.Contains(withArchived.Items, s => s.Id == detail.Id);
    }

    [Fact]
    public async Task 工作会话消息分页_hasMore正确()
    {
        using var bed = CreateBed();
        var detail = await bed.SessionService.CreateAsync(new SessionCreateRequest(Mode.Work, WorkLane));
        await bed.Messages.AppendRangeAsync(
            WorkLane, detail.Id,
            [
                new ChatMessage(ChatRole.User, "一"),
                new ChatMessage(ChatRole.Assistant, "二"),
                new ChatMessage(ChatRole.User, "三"),
                new ChatMessage(ChatRole.Assistant, "四"),
                new ChatMessage(ChatRole.User, "五"),
            ],
            startIndex: 0);

        var latest = await bed.SessionService.GetMessagesAsync(detail.Id, limit: 3);
        Assert.Equal(3, latest.Items.Count);
        Assert.True(latest.HasMore);
        Assert.Equal(["三", "四", "五"], latest.Items.Select(m => m.Text));

        var older = await bed.SessionService.GetMessagesAsync(detail.Id, beforeId: latest.Items[0].Id, limit: 3);
        Assert.Equal(["一", "二"], older.Items.Select(m => m.Text));
        Assert.False(older.HasMore);
    }

    [Fact]
    public async Task RP车道历史_可读且带轮次号_工作车道拒绝()
    {
        using var bed = CreateBed();
        const string rpLane = "desktop:roleplay";
        var now = DateTimeOffset.UtcNow;
        bed.Lanes.Seed(new LaneRecord(
            rpLane, LaneChannel.Desktop, Mode.Roleplay, "desktop", "桌面 RP", null, null, null, now));

        for (var i = 1; i <= 3; i++)
        {
            await bed.Messages.AppendAsync(
                rpLane, sessionId: null, roundNo: i, new ChatMessage(ChatRole.Assistant, $"第{i}轮"));
        }

        var page = await bed.SessionService.GetLaneMessagesAsync(rpLane, limit: 2);
        Assert.Equal(2, page.Items.Count);
        Assert.True(page.HasMore);
        Assert.Equal([2, 3], page.Items.Select(m => m.RoundNo));

        var error = await Assert.ThrowsAsync<RpcException>(
            () => bed.SessionService.GetLaneMessagesAsync(WorkLane));
        Assert.Equal(RpcErrors.InvalidParams, error.Code);
    }

    [Fact]
    public async Task 车道历史_正文剥掉注入标签()
    {
        using var bed = CreateBed();
        const string rpLane = "desktop:roleplay";
        bed.Lanes.Seed(new LaneRecord(
            rpLane, LaneChannel.Desktop, Mode.Roleplay, "desktop", "桌面 RP", null, null, null, DateTimeOffset.UtcNow));

        await bed.Messages.AppendAsync(
            rpLane,
            sessionId: null,
            roundNo: 1,
            new ChatMessage(
                ChatRole.User,
                "<Time>[消息时间: 2026-10-29 14:00 +08:00]</Time><Channel>[渠道: 桌面]</Channel>今天有点累"));

        var page = await bed.SessionService.GetLaneMessagesAsync(rpLane);

        Assert.Equal("今天有点累", Assert.Single(page.Items).Text);
    }

    [Fact]
    public async Task 车道历史_附件结构化返回()
    {
        using var bed = CreateBed();
        const string rpLane = "desktop:roleplay";
        bed.Lanes.Seed(new LaneRecord(
            rpLane, LaneChannel.Desktop, Mode.Roleplay, "desktop", "桌面 RP", null, null, null, DateTimeOffset.UtcNow));

        var attachments = new[]
        {
            new Shoyee.Contracts.Dtos.MessageAttachment(
                "/tmp/inbox/a.png", "a.png", "image/png", 10, AttachmentKind.Image),
        };
        await bed.Messages.AppendAsync(
            rpLane,
            sessionId: null,
            roundNo: 1,
            new ChatMessage(ChatRole.User, "[附件] /tmp/inbox/a.png (image/png)\n看图")
            {
                AdditionalProperties = new AdditionalPropertiesDictionary
                {
                    [ChatMessageMarkers.DisplayText] = "看图",
                    [ChatMessageMarkers.Attachments] = attachments,
                },
            });

        var item = Assert.Single((await bed.SessionService.GetLaneMessagesAsync(rpLane)).Items);

        Assert.Equal("看图", item.Text);
        Assert.Equal("/tmp/inbox/a.png", Assert.Single(item.Attachments!).Path);
    }

    [Fact]
    public async Task 切换当前会话_详情反映指针()
    {
        using var bed = CreateBed();
        var first = await bed.SessionService.CreateAsync(new SessionCreateRequest(Mode.Work, WorkLane, "甲"));
        var second = await bed.SessionService.CreateAsync(new SessionCreateRequest(Mode.Work, WorkLane, "乙"));

        Assert.False((await bed.SessionService.GetAsync(first.Id)).IsActiveForLane);

        await bed.SessionService.SetActiveForLaneAsync(first.Id, WorkLane);

        Assert.True((await bed.SessionService.GetAsync(first.Id)).IsActiveForLane);
        Assert.False((await bed.SessionService.GetAsync(second.Id)).IsActiveForLane);
    }
}
