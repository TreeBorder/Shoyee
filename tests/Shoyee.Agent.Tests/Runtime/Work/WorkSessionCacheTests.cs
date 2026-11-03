using Shoyee.Agent.Composition;
using Shoyee.Agent.Runtime.Work;
using Shoyee.Agent.Tests.TestSupport;
using Shoyee.Sessions.Persistence;
using Xunit;

namespace Shoyee.Agent.Tests.Runtime.Work;

public sealed class WorkSessionCacheTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "shoyee-work-cache-" + Guid.NewGuid().ToString("N"));

    public WorkSessionCacheTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task GetAsync_ReturnsInMemoryInstance_AndEvictReloadsFromStore()
    {
        var repository = new InMemorySessionStateRepository();
        var factory = new WorkAgentFactory(new RecordingChatClient(), _root);
        var agent = factory.CreateAgent();
        var cache = new WorkSessionCache(agent, new ShoyeeAgentSessionStore(repository));
        var session = await factory.CreateSessionAsync(agent, "人设");
        cache.Put("s1", session);

        Assert.Same(session, await cache.GetAsync("s1"));

        await cache.SaveAsync("s1");
        Assert.True(repository.Rows.ContainsKey("s1"));

        Assert.True(cache.Evict("s1"));
        var reloaded = await cache.GetAsync("s1");
        Assert.NotSame(session, reloaded);
    }

    [Fact]
    public async Task GetAsync_MissingSession_Throws()
    {
        var factory = new WorkAgentFactory(new RecordingChatClient(), _root);
        var agent = factory.CreateAgent();
        var cache = new WorkSessionCache(agent, new ShoyeeAgentSessionStore(new InMemorySessionStateRepository()));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => cache.GetAsync("missing"));

        Assert.Contains("missing", error.Message, StringComparison.Ordinal);
    }
}
