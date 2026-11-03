using Shoyee.Contracts.Domain;
using Shoyee.Handlers.Commands;
using Shoyee.Handlers.Tests.TestSupport;
using Xunit;

namespace Shoyee.Handlers.Tests;

/// <summary><c>/title</c>：给当前工作会话改名；对话模式与空参数都拒绝。</summary>
public sealed class TitleCommandTests
{
    [Fact]
    public async Task Work_RenamesCurrentSession()
    {
        var sessions = new FakeSessionService();

        var reply = await new TitleCommand(sessions)
            .HandleAsync(new CommandContext("mm:work", "ses_current", "重构网关", Mode.Work));

        Assert.Equal(("ses_current", "重构网关"), Assert.Single(sessions.Renamed));
        Assert.Contains("已改名：重构网关", reply);
    }

    [Fact]
    public async Task NoArguments_ShowsUsage()
    {
        var sessions = new FakeSessionService();

        var reply = await new TitleCommand(sessions)
            .HandleAsync(new CommandContext("mm:work", "ses_current", "  ", Mode.Work));

        Assert.Contains("用法", reply);
        Assert.Empty(sessions.Renamed);
    }

    [Fact]
    public async Task Roleplay_Refuses()
    {
        var sessions = new FakeSessionService();

        var reply = await new TitleCommand(sessions)
            .HandleAsync(new CommandContext("mm:roleplay", "mm:roleplay", "新标题", Mode.Roleplay));

        Assert.Contains("对话模式", reply);
        Assert.Empty(sessions.Renamed);
    }
}
