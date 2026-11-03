using Microsoft.Extensions.AI;
using Shoyee.Agent.Composition;
using Shoyee.Agent.Tests.TestSupport;
using Shoyee.Sessions.Roleplay;
using Xunit;

namespace Shoyee.Agent.Tests.Composition;

public sealed class RoleplayAgentFactoryTests
{
    [Fact]
    public async Task RoleplayAgent_HasRecorderButNoFrameworkHistory()
    {
        var client = new RecordingChatClient();
        var recorder = new RoundRecorder(new NoopRoundRepository());
        var factory = new RoleplayAgentFactory(client);
        var agent = factory.CreateAgent(recorder);
        var session = await agent.CreateSessionAsync();
        var messages = new[]
        {
            new ChatMessage(ChatRole.System, "背景"),
            new ChatMessage(ChatRole.User, "你好"),
        };

        await agent.RunAsync(messages, session);

        var sent = Assert.Single(client.Calls);
        Assert.Equal(["背景", "你好"], sent.Select(m => m.Text));
        Assert.Equal("应答", Assert.Single(recorder.TakeResponse(session)).Text);

        // 第二轮只带本轮组装的消息，框架不偷偷补上一轮历史
        await agent.RunAsync([new ChatMessage(ChatRole.User, "再来")], session);
        Assert.Equal(["再来"], client.Calls[1].Select(m => m.Text));
    }
}
