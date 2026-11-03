using Microsoft.Extensions.AI;
using Shoyee.Agent.Composition;
using Shoyee.Agent.Runtime.Work;
using Shoyee.Agent.Tests.TestSupport;
using Shoyee.Sessions.Persistence;
using Xunit;

namespace Shoyee.Agent.Tests.Runtime.Work;

public sealed class WorkTurnRunnerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "shoyee-work-turn-" + Guid.NewGuid().ToString("N"));

    public WorkTurnRunnerTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_SavesTurn_AndResumeSeesHistory()
    {
        var client = new RecordingChatClient();
        var factory = new WorkAgentFactory(client, _root);
        var agent = factory.CreateAgent();
        var repository = new InMemorySessionStateRepository();
        var cache = new WorkSessionCache(agent, new ShoyeeAgentSessionStore(repository));
        var runner = new WorkTurnRunner(agent, factory, cache);
        var session = await factory.CreateSessionAsync(agent, "工作人设");
        cache.Put("s1", session);

        var response = await runner.RunAsync("s1", "test-lane", new ChatMessage(ChatRole.User, "第一句"));

        Assert.Equal("应答", response.Text);
        Assert.True(repository.Rows.ContainsKey("s1"));

        cache.Evict("s1");
        await runner.RunAsync("s1", "test-lane", new ChatMessage(ChatRole.User, "第二句"));

        var resumed = client.Calls[1];
        Assert.Equal("工作人设", resumed[0].Text);
        Assert.Equal(["第一句", "应答", "第二句"], resumed.Skip(1).Select(m => m.Text));
    }

    [Fact]
    public async Task RunAsync_落盘后补抄转录投影()
    {
        var client = new RecordingChatClient();
        var factory = new WorkAgentFactory(client, _root);
        var agent = factory.CreateAgent();
        var repository = new InMemorySessionStateRepository();
        var messages = new RecordingMessageRepository();
        var cache = new WorkSessionCache(agent, new ShoyeeAgentSessionStore(repository));
        var runner = new WorkTurnRunner(agent, factory, cache, new SessionTranscript(messages));
        var session = await factory.CreateSessionAsync(agent, "工作人设");
        cache.Put("s1", session);

        await runner.RunAsync("s1", "mattermost:work", new ChatMessage(ChatRole.User, "第一句"));

        Assert.Equal(["工作人设", "第一句", "应答"], messages.Rows.Select(r => r.Content));
        Assert.All(messages.Rows, r => Assert.Equal("mattermost:work", r.LaneId));
        Assert.Equal(3, await messages.GetProjectedIndexAsync("s1"));
    }
}
