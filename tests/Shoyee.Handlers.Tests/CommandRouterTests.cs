using Shoyee.Handlers;
using Shoyee.Handlers.Commands;
using Shoyee.Handlers.Tests.TestSupport;
using Xunit;

namespace Shoyee.Handlers.Tests;

/// <summary>指令路由：只拦已注册指令，参数解析，未知斜杠放行。</summary>
public sealed class CommandRouterTests
{
    [Theory]
    [InlineData("/approve", "approve")]
    [InlineData("  /Approve  ", "Approve")]
    [InlineData("/approve always", "approve")]
    [InlineData("/stop\n", "stop")]
    public void ParseCommandName_HandlesLeadingWhitespaceAndArguments(string text, string expected)
        => Assert.Equal(expected, CommandRouter.ParseCommandName(text));

    [Theory]
    [InlineData("你好")]
    [InlineData("/")]
    [InlineData("http://example.com/x")]
    public void ParseCommandName_NonCommand_IsEmpty(string text)
        => Assert.Equal(string.Empty, CommandRouter.ParseCommandName(text));

    [Theory]
    [InlineData("/approve always", "always")]
    [InlineData("/approve", "")]
    [InlineData("/reject   因为不安全 ", "因为不安全")]
    public void ParseArguments_ReturnsTrimmedRest(string text, string expected)
        => Assert.Equal(expected, CommandRouter.ParseArguments(text));

    [Fact]
    public void IsKnownCommand_OnlyRegisteredNames()
    {
        var router = new CommandRouter([new EchoCommand()]);

        Assert.True(router.IsKnownCommand("/echo 你好"));
        Assert.True(router.IsKnownCommand("/ECHO"));
        // 未知斜杠当普通消息：聊天里打个路径不该被劫持
        Assert.False(router.IsKnownCommand("/volume 你好"));
        Assert.False(router.IsKnownCommand("你好"));
    }

    [Fact]
    public async Task RouteAsync_PassesArgumentsAndContext()
    {
        var router = new CommandRouter([new EchoCommand()]);

        var reply = await router.RouteAsync(new CommandContext("lane_1", "ses_1", string.Empty), "/echo 你好");

        Assert.Equal("lane_1|ses_1|你好", reply);
    }

    [Fact]
    public async Task RouteAsync_UnknownCommand_Throws()
    {
        var router = new CommandRouter([new EchoCommand()]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => router.RouteAsync(new CommandContext("lane_1", "ses_1", string.Empty), "/nope"));
    }

    [Fact]
    public async Task HelpCommand_ListsEveryRegisteredCommand()
    {
        var router = new CommandRouter([new EchoCommand()]);
        var help = new HelpCommand(new SingleServiceProvider(router));

        var reply = await help.HandleAsync(new CommandContext("lane_1", "ses_1", string.Empty));

        Assert.Contains("/echo", reply);
        Assert.Contains(EchoCommand.Usage, reply);
    }

    private sealed class EchoCommand : ICommandHandler
    {
        public const string Usage = "/echo 回显参数";

        public string CommandName => "echo";

        public string Description => Usage;

        public Task<string> HandleAsync(CommandContext context, CancellationToken cancellationToken = default)
            => Task.FromResult($"{context.LaneId}|{context.TargetId}|{context.Arguments}");
    }

    private sealed class SingleServiceProvider(CommandRouter router) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(CommandRouter) ? router : null;
    }
}
