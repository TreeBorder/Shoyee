using Microsoft.Extensions.AI;
using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Options;
using Shoyee.Storage.Abstractions;
using Shoyee.Tools.Composition;
using Xunit;

namespace Shoyee.Tools.Tests.Composition;

public sealed class ToolsetBuilderTests
{
    [Fact]
    public void Work_GetsWorkTools_NotRoleplayTools()
    {
        var config = Config();
        var builder = new ToolsetBuilder(config, Services(config));

        var work = Names(builder.Build(ToolProfile.Work));

        Assert.Contains("terminal", work);
        Assert.Contains("file_access_write", work);
        Assert.Contains("process_start", work);
        Assert.Contains("web_search", work);
        Assert.DoesNotContain("get_states", work);
    }

    [Fact]
    public void Roleplay_GetsSharedReadTools_AndStateTools_NotShell()
    {
        var config = Config();
        var builder = new ToolsetBuilder(config, Services(config));

        var roleplay = Names(builder.Build(ToolProfile.Roleplay));

        Assert.Contains("file_access_read", roleplay);
        Assert.Contains("file_access_write", roleplay);
        Assert.Contains("web_search", roleplay);
        Assert.Contains("session_search", roleplay);
        Assert.Contains("get_states", roleplay);
        Assert.Contains("update_state", roleplay);
        Assert.DoesNotContain("terminal", roleplay);
        Assert.DoesNotContain("process_start", roleplay);
        Assert.Empty(builder.Build(ToolProfile.Roleplay).AutoApprovalRules);
    }

    [Fact]
    public void WriteTools_AreApprovalWrapped_ReadToolsAreNot()
    {
        var config = Config();
        var tools = new ToolsetBuilder(config, Services(config)).Build(ToolProfile.Work).Tools;

        Assert.IsType<ApprovalRequiredAIFunction>(
            tools.Single(t => t is AIFunction f && f.Name == "file_access_write"));
        Assert.IsNotType<ApprovalRequiredAIFunction>(
            tools.Single(t => t is AIFunction f && f.Name == "file_access_read"));
        Assert.Equal(2, new ToolsetBuilder(config, Services(config)).Build(ToolProfile.Work).AutoApprovalRules.Count);
    }

    [Fact]
    public void UnattendedWork_DoesNotWrapApproval()
    {
        var config = Config();
        var tools = new ToolsetBuilder(config, Services(config))
            .Build(ToolProfile.Work, requireApproval: false)
            .Tools;

        Assert.IsNotType<ApprovalRequiredAIFunction>(
            tools.Single(t => t is AIFunction f && f.Name == "file_access_write"));
        Assert.Contains(tools, t => t is AIFunction f && f.Name == "terminal");
    }

    [Fact]
    public void Disabled_RemovesToolFromItsProfile()
    {
        var config = Config() with { Tools = new ToolOptions(Disabled: ["file_access_read", "get_states"]) };
        var builder = new ToolsetBuilder(config, Services(config));

        Assert.DoesNotContain("file_access_read", Names(builder.Build(ToolProfile.Work)));
        Assert.DoesNotContain("get_states", Names(builder.Build(ToolProfile.Roleplay)));
    }

    [Fact]
    public void Catalog_ListsBothProfilesAndHonorsDisabled()
    {
        var config = Config();
        var items = new ToolCatalog(new ToolsetBuilder(config, Services(config))).List();

        var terminal = items.Single(item => item.Name == "terminal");
        Assert.True(terminal.Enabled);
        Assert.Equal("work", terminal.Scope);
        Assert.True(terminal.RequiresApproval);
        Assert.Contains(items, item => item.Name == "get_states" && item.Scope == "roleplay");
        Assert.Equal(items.Count, items.Select(static item => item.Name).Distinct().Count());

        var disabledConfig = config with { Tools = config.Tools with { Disabled = ["terminal"] } };
        var disabled = new ToolCatalog(new ToolsetBuilder(disabledConfig, Services(disabledConfig))).List();
        Assert.False(disabled.Single(item => item.Name == "terminal").Enabled);
    }

    private static ToolsetServices Services(WorkspaceConfig config)
        => new(
            SessionSearch: new FakeSessionSearch(),
            Processes: new Shoyee.Tools.Process.ProcessRegistry(
                Path.Combine(config.Root, ".processes"), config.Root),
            Web: new NeverCalledWebClient(),
            States: new FakeStateRepository());

    private static List<string> Names(Toolset toolset)
        => toolset.Tools.OfType<AIFunction>().Select(static f => f.Name).ToList();

    private static WorkspaceConfig Config()
        => new(
            Path.Combine(Path.GetTempPath(), "shoyee-toolset-" + Guid.NewGuid().ToString("N")),
            new ModelProfiles(
                new ModelEndpoint("http://localhost", "m", "KEY"),
                new ModelEndpoint("http://localhost", "s", "KEY")),
            new ChannelAccounts(),
            new ProactiveOptions(new HeartbeatOptions()),
            new ToolOptions());

    private sealed class NeverCalledWebClient : Shoyee.Web.IWebProvider
    {
        public Task<Shoyee.Web.WebSearchResponse> SearchAsync(
            string query,
            int count = 10,
            string? oriQuestion = null,
            string? localization = null,
            bool needContent = false,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("不应调用");

        public Task<Shoyee.Web.WebFetchResponse> FetchAsync(
            string url,
            bool decodeEscapes = true,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("不应调用");
    }

    private sealed class FakeSessionSearch : Shoyee.Memory.History.ISessionSearch
    {
        public Task<IReadOnlyList<Shoyee.Contracts.Dtos.SearchHit>> SearchAsync(
            string query,
            Shoyee.Contracts.Dtos.SessionSearchFilter? filter = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Shoyee.Contracts.Dtos.SearchHit>>([]);

        public Task<IReadOnlyList<Shoyee.Storage.Abstractions.MessageRecord>> ReadAsync(
            string sessionId,
            long? afterId = null,
            int take = 100,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Shoyee.Storage.Abstractions.MessageRecord>>([]);
    }

    private sealed class FakeStateRepository : IStateRepository
    {
        private readonly Dictionary<string, StateRecord> _states = new()
        {
            ["mood"] = new("mood", "心情", "平静", null, true, DateTimeOffset.UtcNow),
        };

        public Task<IReadOnlyList<StateRecord>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<StateRecord>>(_states.Values.ToList());

        public Task<StateRecord?> GetAsync(string key, CancellationToken cancellationToken = default)
            => Task.FromResult(_states.TryGetValue(key, out var state) ? state : null);

        public Task<bool> SetAsync(string key, string value, bool byAgent = false, CancellationToken cancellationToken = default)
        {
            if (!_states.TryGetValue(key, out var state) || (byAgent && !state.AgentEditable))
            {
                return Task.FromResult(false);
            }

            _states[key] = state with { Value = value, UpdatedAt = DateTimeOffset.UtcNow };
            return Task.FromResult(true);
        }

        public Task<StateRecord> UpsertAsync(StateRecord record, CancellationToken cancellationToken = default)
        {
            _states[record.Key] = record;
            return Task.FromResult(record);
        }

        public Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
            => Task.FromResult(_states.Remove(key));

        public Task SeedIfEmptyAsync(IReadOnlyList<StateRecord> defaults, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
