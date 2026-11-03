using Microsoft.Extensions.DependencyInjection;
using Shoyee.Contracts.Domain;
using Shoyee.Handlers.Commands;
using Xunit;

namespace Shoyee.Handlers.Tests;

/// <summary><c>/help</c>：输出 Markdown 表格，说明取各指令的短描述。</summary>
public sealed class HelpCommandTests
{
    [Fact]
    public async Task RendersMarkdownTableOfRegisteredCommands()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICommandHandler>(new StubHandler("list", "会话列表"));
        services.AddSingleton<ICommandHandler>(new StubHandler("stop", "停下当前"));
        services.AddSingleton<CommandRouter>();
        await using var provider = services.BuildServiceProvider();

        var reply = await new HelpCommand(provider)
            .HandleAsync(new CommandContext("mm:work", "ses_a", string.Empty, Mode.Work));

        Assert.Contains("| 指令 | 说明 |", reply);
        Assert.Contains("| `/list` | 会话列表 |", reply);
        Assert.Contains("| `/stop` | 停下当前 |", reply);
        // 按指令名排序，list 在 stop 之前
        Assert.True(reply.IndexOf("`/list`", StringComparison.Ordinal) < reply.IndexOf("`/stop`", StringComparison.Ordinal));
    }

    private sealed class StubHandler(string name, string description) : ICommandHandler
    {
        public string CommandName => name;

        public string Description => description;

        public Task<string> HandleAsync(CommandContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(string.Empty);
    }
}
