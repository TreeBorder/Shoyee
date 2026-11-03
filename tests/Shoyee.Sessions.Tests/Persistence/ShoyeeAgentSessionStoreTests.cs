using Microsoft.Agents.AI;
using Shoyee.Sessions.Persistence;
using Shoyee.Sessions.Tests.TestSupport;
using Shoyee.Storage.Abstractions;
using Xunit;

namespace Shoyee.Sessions.Tests.Persistence;

public sealed class ShoyeeAgentSessionStoreTests
{
    [Fact]
    public async Task SaveSessionAsync_StoresSerializedJson_WithOrderedPartitionKey()
    {
        var repository = new InMemorySessionStateRepository();
        var store = new ShoyeeAgentSessionStore(repository);
        var agent = StubChatClient.CreateAgent();
        var session = await agent.CreateSessionAsync();
        session.StateBag.SetValue("marker", "hello");

        var key = new AgentSessionStoreKey("s1", new Dictionary<string, string> { ["b"] = "2", ["a"] = "1" });
        await store.SaveSessionAsync(agent, key, session);

        var stored = Assert.Single(repository.Rows);
        Assert.Equal("s1|a=1&b=2", stored.Key);
        var loaded = await store.GetSessionAsync(agent, key);
        Assert.NotNull(loaded);
        Assert.Equal("hello", loaded!.StateBag.GetValue<string>("marker"));
    }

    [Fact]
    public async Task SaveSessionAsync_WithoutPartitions_UsesPlainSessionId()
    {
        var repository = new InMemorySessionStateRepository();
        var store = new ShoyeeAgentSessionStore(repository);
        var agent = StubChatClient.CreateAgent();

        await store.SaveSessionAsync(agent, new AgentSessionStoreKey("plain"), await agent.CreateSessionAsync());

        Assert.True(repository.Rows.ContainsKey("plain"));
    }

    [Fact]
    public async Task GetSessionAsync_MissingKey_ReturnsNull()
    {
        var store = new ShoyeeAgentSessionStore(new InMemorySessionStateRepository());

        var session = await store.GetSessionAsync(StubChatClient.CreateAgent(), new AgentSessionStoreKey("missing"));

        Assert.Null(session);
    }

    [Fact]
    public async Task GetSessionAsync_ReturnsIndependentCopies()
    {
        var store = new ShoyeeAgentSessionStore(new InMemorySessionStateRepository());
        var agent = StubChatClient.CreateAgent();
        var key = new AgentSessionStoreKey("s1");
        await store.SaveSessionAsync(agent, key, await agent.CreateSessionAsync());

        var first = await store.GetSessionAsync(agent, key);
        var second = await store.GetSessionAsync(agent, key);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotSame(first, second);
    }

    [Fact]
    public async Task SaveSessionAsync_OverwritesSameKey()
    {
        var repository = new InMemorySessionStateRepository();
        var store = new ShoyeeAgentSessionStore(repository);
        var agent = StubChatClient.CreateAgent();
        var key = new AgentSessionStoreKey("s1");

        var first = await agent.CreateSessionAsync();
        first.StateBag.SetValue("marker", "v1");
        await store.SaveSessionAsync(agent, key, first);

        var second = await agent.CreateSessionAsync();
        second.StateBag.SetValue("marker", "v2");
        await store.SaveSessionAsync(agent, key, second);

        Assert.Single(repository.Rows);
        var loaded = await store.GetSessionAsync(agent, key);
        Assert.NotNull(loaded);
        Assert.Equal("v2", loaded!.StateBag.GetValue<string>("marker"));
    }

    private sealed class InMemorySessionStateRepository : ISessionStateRepository
    {
        public Dictionary<string, string> Rows { get; } = new(StringComparer.Ordinal);

        public Task<string?> GetAsync(string storageKey, CancellationToken cancellationToken = default)
            => Task.FromResult(Rows.TryGetValue(storageKey, out var json) ? json : null);

        public Task SaveAsync(
            string storageKey,
            string stateJson,
            DateTimeOffset? at = null,
            CancellationToken cancellationToken = default)
        {
            Rows[storageKey] = stateJson;
            return Task.CompletedTask;
        }

        public Task<bool> DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
            => Task.FromResult(Rows.Remove(storageKey));
    }
}
