using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Shoyee.Agent.Composition;
using Shoyee.Agent.Tests.TestSupport;
using Xunit;

namespace Shoyee.Agent.Tests.Composition;

public sealed class WorkAgentFactoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "shoyee-work-agent-" + Guid.NewGuid().ToString("N"));

    public WorkAgentFactoryTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task WorkAgent_IsHarness_AndFirstSystemIsSeededOnce()
    {
        var client = new RecordingChatClient();
        var factory = new WorkAgentFactory(client, _root);
        var agent = factory.CreateAgent();
        Assert.IsType<HarnessAgent>(agent);

        var session = await factory.CreateSessionAsync(agent, "工作人设");
        await agent.RunAsync("第一句", session);
        await agent.RunAsync("第二句", session);

        Assert.Equal(2, client.Calls.Count);
        var second = client.Calls[1];
        Assert.Equal("工作人设", second[0].Text);
        Assert.Equal(ChatRole.System, second[0].Role);
        Assert.Equal(["第一句", "应答", "第二句"], second.Skip(1).Select(m => m.Text));
    }

    [Fact]
    public async Task WorkAgent_CarriesAssembledTools()
    {
        var client = new RecordingChatClient();
        var probe = AIFunctionFactory.Create(
            static () => "ok",
            new AIFunctionFactoryOptions { Name = "probe_tool", Description = "测试探针" });
        var factory = new WorkAgentFactory(
            client,
            _root,
            toolset: new Shoyee.Tools.Composition.Toolset([probe], []));
        var agent = factory.CreateAgent();
        var session = await factory.CreateSessionAsync(agent, "工作人设");

        await agent.RunAsync("干点活", session);

        Assert.Contains(client.OptionCalls[0]!.Tools!, tool => tool is AIFunction f && f.Name == "probe_tool");
    }
}
