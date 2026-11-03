using Microsoft.Extensions.Logging.Abstractions;
using Shoyee.Contracts.Options;
using Shoyee.Contracts.Rpc;
using Shoyee.Daemon.Configuration;
using Shoyee.Daemon.Tests.TestSupport;
using Shoyee.Tools.Composition;
using Xunit;

namespace Shoyee.Daemon.Tests.Configuration;

/// <summary><c>config.reload</c> 的分段比较：只有不碰请求前缀的项能热更新。</summary>
public sealed class ConfigReloaderTests
{
    [Fact]
    public void Compare_DeniedPaths_IsHotReloadable()
    {
        var current = Config();
        var next = current with
        {
            Tools = current.Tools with { DeniedPaths = ["assets/images"] },
        };

        var (changed, restart) = ConfigSectionDiff.Compare(current, next);

        Assert.Equal(["tools.deniedPaths"], changed);
        Assert.Empty(restart);
    }

    [Fact]
    public void Compare_ToolsDisabled_RequiresRestart()
    {
        var current = Config();
        var next = current with { Tools = new ToolOptions(Disabled: ["terminal"]) };

        var (changed, restart) = ConfigSectionDiff.Compare(current, next);

        Assert.Empty(changed);
        Assert.Equal(["tools"], restart);
    }

    [Fact]
    public void Compare_ModelsAndShellTimeout_RequireRestart()
    {
        var current = Config();
        var next = current with
        {
            Models = current.Models with
            {
                Main = current.Models.Main with { ModelId = "other" },
            },
            Shell = new ShellOptions(TimeoutSeconds: 30),
        };

        var (changed, restart) = ConfigSectionDiff.Compare(current, next);

        Assert.Empty(changed);
        Assert.Equal(["models", "shell"], restart);
    }

    [Fact]
    public void Compare_ShellDenyPatterns_IsHotReloadable()
    {
        var current = Config() with { Shell = new ShellOptions(DenyPatterns: ["rm -rf"]) };
        var next = current with { Shell = new ShellOptions(DenyPatterns: ["rm -rf", "shutdown"]) };

        var (changed, restart) = ConfigSectionDiff.Compare(current, next);

        Assert.Equal(["shell.denyPatterns"], changed);
        Assert.Empty(restart);
    }

    [Fact]
    public void Compare_Attachments_IsHotReloadable()
    {
        var current = Config();
        var next = current with { Attachments = new AttachmentOptions(AutoAnalyze: false) };

        var (changed, restart) = ConfigSectionDiff.Compare(current, next);

        Assert.Equal(["attachments"], changed);
        Assert.Empty(restart);
    }

    [Fact]
    public void Compare_ContextCompaction_RequiresRestart()
    {
        var current = Config();
        var next = current with
        {
            Context = new ContextOptions(new CompactionOptions(Enabled: true, TriggerTokens: 800_000)),
        };

        var (changed, restart) = ConfigSectionDiff.Compare(current, next);

        Assert.Empty(changed);
        Assert.Equal(["context"], restart);
    }

    [Fact]
    public void Compare_ChannelsChanged_RequiresRestart()
    {
        var current = Config();
        var next = current with
        {
            Channels = new ChannelAccounts(
                new MattermostAccounts(
                    new MattermostAccount("http://127.0.0.1:8065", "RP_TOKEN"),
                    new MattermostAccount("http://127.0.0.1:8065", "WORK_TOKEN"))),
        };

        var (changed, restart) = ConfigSectionDiff.Compare(current, next);

        Assert.Empty(changed);
        Assert.Equal(["channels"], restart);
    }

    [Fact]
    public void Compare_Unchanged_IsEmpty()
    {
        var (changed, restart) = ConfigSectionDiff.Compare(Config(), Config());

        Assert.Empty(changed);
        Assert.Empty(restart);
    }

    [Fact]
    public async Task Reload_AppliesHotKeysAndReportsRestart()
    {
        using var dir = new TempDir();
        WriteConfig(dir.Path, deniedPaths: "[]", channels: "{}");
        var store = new WorkspaceConfigStore(WorkspaceConfigLoader.Load(dir.Path));
        var toolsets = new ToolsetBuilder(store.Current);
        var reloader = new ConfigReloader(store, toolsets, NullLogger<ConfigReloader>.Instance);

        WriteConfig(
            dir.Path,
            deniedPaths: """["private"]""",
            channels: """
                {
                  "mattermost": {
                    "roleplay": { "baseUrl": "http://127.0.0.1:8065", "tokenEnv": "RP_TOKEN" },
                    "work": { "baseUrl": "http://127.0.0.1:8065", "tokenEnv": "WORK_TOKEN" }
                  }
                }
                """);

        var result = await reloader.ReloadAsync();

        Assert.Equal(["tools.deniedPaths"], result.Changed);
        Assert.Equal(["channels"], result.RestartRequired);
        Assert.Equal(["private"], store.Current.Tools.DeniedPaths);
    }

    [Fact]
    public async Task Reload_NothingChanged_KeepsStore()
    {
        using var dir = new TempDir();
        WriteConfig(dir.Path);
        var store = new WorkspaceConfigStore(WorkspaceConfigLoader.Load(dir.Path));
        var before = store.Current;
        var reloader = new ConfigReloader(
            store, new ToolsetBuilder(before), NullLogger<ConfigReloader>.Instance);

        var result = await reloader.ReloadAsync();

        Assert.Empty(result.Changed);
        Assert.Empty(result.RestartRequired);
        Assert.NotSame(before, store.Current);
    }

    [Fact]
    public async Task Reload_MalformedFile_ThrowsInvalidParams()
    {
        using var dir = new TempDir();
        WriteConfig(dir.Path);
        var store = new WorkspaceConfigStore(WorkspaceConfigLoader.Load(dir.Path));
        var reloader = new ConfigReloader(
            store, new ToolsetBuilder(store.Current), NullLogger<ConfigReloader>.Instance);
        File.WriteAllText(Path.Combine(dir.Path, "config.json"), "{ not json");

        var ex = await Assert.ThrowsAsync<RpcException>(() => reloader.ReloadAsync());

        Assert.Equal(RpcErrors.InvalidParams, ex.Code);
    }

    private static WorkspaceConfig Config()
        => new(
            "/tmp/shoyee-reload",
            new ModelProfiles(
                new ModelEndpoint("http://127.0.0.1:1/v1", "main", "MAIN_KEY"),
                new ModelEndpoint("http://127.0.0.1:1/v1", "small", "SMALL_KEY")),
            new ChannelAccounts(),
            new ProactiveOptions(new HeartbeatOptions()),
            new ToolOptions());

    private static void WriteConfig(
        string root,
        string deniedPaths = "[]",
        string channels = "{}")
        => File.WriteAllText(
            Path.Combine(root, "config.json"),
            $$"""
              {
                "models": {
                  "main": { "baseUrl": "http://127.0.0.1:1/v1", "modelId": "main", "apiKeyEnv": "MAIN_KEY" },
                  "small": { "baseUrl": "http://127.0.0.1:1/v1", "modelId": "small", "apiKeyEnv": "SMALL_KEY" }
                },
                "channels": {{channels}},
                "proactive": { "heartbeat": { "enabled": false } },
                "tools": { "deniedPaths": {{deniedPaths}} }
              }
              """);
}
