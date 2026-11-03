using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;
using Shoyee.Handlers.Commands;
using Shoyee.Handlers.Tests.TestSupport;
using Xunit;

namespace Shoyee.Handlers.Tests;

/// <summary><c>/list</c>：工作模式列本 Lane 的会话并标出当前会话；对话模式直接拒绝。</summary>
public sealed class ListCommandTests
{
    [Fact]
    public async Task Work_RendersTableAndMarksActiveSession()
    {
        var sessions = new FakeSessionService();
        sessions.Items.AddRange(
        [
            Summary("ses_a", "重构网关", DateTimeOffset.UtcNow, 128),
            Summary("ses_b", "周报", DateTimeOffset.UtcNow.AddDays(-1), 34),
        ]);

        var reply = await new ListCommand(sessions)
            .HandleAsync(new CommandContext("mm:work", "ses_b", string.Empty, Mode.Work));

        Assert.Contains("| # | 会话 | 最后活跃 | 消息 |", reply);
        Assert.Contains("| 1 | 重构网关 |", reply);
        Assert.Contains("| ⭐ 2 | 周报 |", reply);
    }

    [Fact]
    public async Task Work_MarksArchivedSessions()
    {
        var sessions = new FakeSessionService();
        sessions.Items.Add(Summary("ses_a", "旧话题", DateTimeOffset.UtcNow, 3, archived: true));

        var reply = await new ListCommand(sessions)
            .HandleAsync(new CommandContext("mm:work", "ses_a", string.Empty, Mode.Work));

        Assert.Contains("旧话题（已归档）", reply);
    }

    [Fact]
    public async Task Work_EmptyListSaysSo()
    {
        var reply = await new ListCommand(new FakeSessionService())
            .HandleAsync(new CommandContext("mm:work", "ses_a", string.Empty, Mode.Work));

        Assert.Contains("还没有会话", reply);
    }

    [Fact]
    public async Task Roleplay_RefusesWithoutListing()
    {
        var sessions = new FakeSessionService();
        var reply = await new ListCommand(sessions)
            .HandleAsync(new CommandContext("mm:roleplay", "mm:roleplay", string.Empty, Mode.Roleplay));

        Assert.Contains("对话模式", reply);
        Assert.Equal(0, sessions.ListCalls);
    }

    internal static SessionSummary Summary(
        string id,
        string title,
        DateTimeOffset lastActiveAt,
        int messageCount,
        bool archived = false)
        => new(id, Mode.Work, LaneChannel.Mattermost, "mm:work", title, archived, lastActiveAt, lastActiveAt, messageCount);
}
