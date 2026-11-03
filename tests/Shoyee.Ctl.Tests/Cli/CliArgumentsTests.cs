using Shoyee.Ctl.Cli;
using Xunit;

namespace Shoyee.Ctl.Tests.Cli;

/// <summary>ctl 的参数形状很窄，但菜单栏与终端都靠它——拼错时要报错，不要静默当默认值。</summary>
public sealed class CliArgumentsTests
{
    [Fact]
    public void 位置参数与选项分开()
    {
        var parsed = CliArguments.Parse(["notify", "--title", "标题", "--body=正文", "--json"]);

        Assert.Equal("notify", parsed.Command);
        Assert.Equal("标题", parsed.Get("title"));
        Assert.Equal("正文", parsed.Get("body"));
        Assert.True(parsed.Has("json"));
        Assert.Null(parsed.Get("missing"));
    }

    [Fact]
    public void 没有命令时Command为null()
    {
        var parsed = CliArguments.Parse(["--json"]);

        Assert.Null(parsed.Command);
        Assert.True(parsed.Has("json"));
    }

    [Fact]
    public void 双横线之后的参数不再当选项()
    {
        var parsed = CliArguments.Parse(["index", "remove", "--", "--weird-key"]);

        Assert.Equal(["index", "remove", "--weird-key"], parsed.Positionals);
        Assert.False(parsed.Has("weird-key"));
    }
}
