using Shoyee.Contracts.Domain;
using Shoyee.Handlers.Commands;
using Shoyee.Handlers.Tests.TestSupport;
using Xunit;

namespace Shoyee.Handlers.Tests;

/// <summary><c>/resume</c>：按序号/id 前缀切回会话；归档拒绝、打不开不动指针。</summary>
public sealed class ResumeCommandTests
{
    [Fact]
    public async Task ByIndex_OpensThenSwitchesPointer()
    {
        var sessions = new FakeSessionService();
        sessions.Items.AddRange(
        [
            ListCommandTests.Summary("ses_a", "甲", DateTimeOffset.UtcNow, 2),
            ListCommandTests.Summary("ses_b", "乙", DateTimeOffset.UtcNow, 5),
        ]);

        var reply = await new ResumeCommand(sessions)
            .HandleAsync(new CommandContext("mm:work", "ses_a", "2", Mode.Work));

        Assert.Equal("ses_b", Assert.Single(sessions.Opened));
        Assert.Equal(("ses_b", "mm:work"), Assert.Single(sessions.Activated));
        Assert.Contains("已切回：乙", reply);
    }

    [Fact]
    public async Task ByPrefix_MatchesCaseInsensitively()
    {
        var sessions = new FakeSessionService();
        sessions.Items.Add(ListCommandTests.Summary("ses_ABC123", "甲", DateTimeOffset.UtcNow, 2));

        await new ResumeCommand(sessions)
            .HandleAsync(new CommandContext("mm:work", "ses_other", "ses_abc", Mode.Work));

        Assert.Equal("ses_ABC123", Assert.Single(sessions.Opened));
    }

    [Fact]
    public async Task Archived_RefusesWithoutOpening()
    {
        var sessions = new FakeSessionService();
        sessions.Items.Add(ListCommandTests.Summary("ses_a", "旧", DateTimeOffset.UtcNow, 2, archived: true));

        var reply = await new ResumeCommand(sessions)
            .HandleAsync(new CommandContext("mm:work", "ses_a", "1", Mode.Work));

        Assert.Contains("已归档", reply);
        Assert.Empty(sessions.Opened);
        Assert.Empty(sessions.Activated);
    }

    [Fact]
    public async Task OutOfRangeIndex_Refuses()
    {
        var sessions = new FakeSessionService();
        sessions.Items.Add(ListCommandTests.Summary("ses_a", "甲", DateTimeOffset.UtcNow, 2));

        var reply = await new ResumeCommand(sessions)
            .HandleAsync(new CommandContext("mm:work", "ses_a", "9", Mode.Work));

        Assert.Contains("没有第 9 个会话", reply);
        Assert.Empty(sessions.Opened);
    }

    [Fact]
    public async Task NoArguments_ShowsUsage()
    {
        var reply = await new ResumeCommand(new FakeSessionService())
            .HandleAsync(new CommandContext("mm:work", "ses_a", string.Empty, Mode.Work));

        Assert.Contains("用法", reply);
    }

    [Fact]
    public async Task Roleplay_Refuses()
    {
        var sessions = new FakeSessionService();
        var reply = await new ResumeCommand(sessions)
            .HandleAsync(new CommandContext("mm:roleplay", "mm:roleplay", "1", Mode.Roleplay));

        Assert.Contains("对话模式", reply);
        Assert.Equal(0, sessions.ListCalls);
    }
}
