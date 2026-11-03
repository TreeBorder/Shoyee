using Shoyee.Contracts.Domain;
using Shoyee.Handlers.Commands;
using Shoyee.Handlers.Tests.TestSupport;
using Shoyee.Storage.Abstractions;
using Xunit;

namespace Shoyee.Handlers.Tests;

/// <summary><c>/state</c>：只角色模式，直接读事实表，不问模型。</summary>
public sealed class StateCommandTests
{
    [Fact]
    public async Task Roleplay_ListsAllStatesAsTable()
    {
        var states = new FakeStateRepository();
        states.Items.AddRange(
        [
            Record("mood", "心情", "愉快", "当作语气参考"),
            Record("energy", "精力", "72", "0-100"),
        ]);

        var reply = await new StateCommand(states)
            .HandleAsync(new CommandContext("mm:roleplay", "mm:roleplay", string.Empty, Mode.Roleplay));

        Assert.Contains("| 状态 | 值 | 说明 |", reply);
        Assert.Contains("| 精力 | 72 | 0-100 |", reply);
        Assert.Contains("| 心情 | 愉快 | 当作语气参考 |", reply);
    }

    [Fact]
    public async Task Roleplay_SingleKeyIncludesUpdatedAt()
    {
        var states = new FakeStateRepository();
        states.Items.Add(Record("mood", "心情", "愉快", "当作语气参考"));

        var reply = await new StateCommand(states)
            .HandleAsync(new CommandContext("mm:roleplay", "mm:roleplay", "mood", Mode.Roleplay));

        Assert.Contains("| 状态 | 值 | 说明 | 更新 |", reply);
        Assert.Contains("| 心情 | 愉快 |", reply);
    }

    [Fact]
    public async Task Roleplay_UnknownKeySaysSo()
    {
        var reply = await new StateCommand(new FakeStateRepository())
            .HandleAsync(new CommandContext("mm:roleplay", "mm:roleplay", "nope", Mode.Roleplay));

        Assert.Contains("没有状态 nope", reply);
    }

    [Fact]
    public async Task Work_Refuses()
    {
        var reply = await new StateCommand(new FakeStateRepository())
            .HandleAsync(new CommandContext("mm:work", "ses_a", string.Empty, Mode.Work));

        Assert.Contains("角色模式", reply);
    }

    private static StateRecord Record(string key, string name, string value, string description)
        => new(key, name, value, description, true, DateTimeOffset.UtcNow);
}
