using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Options;
using Shoyee.Contracts.Rpc;
using Shoyee.Daemon.Configuration;
using Shoyee.Daemon.Tests.TestSupport;
using Shoyee.Tools.Composition;
using Xunit;

namespace Shoyee.Daemon.Tests.Configuration;

/// <summary><c>config.set</c>：局部合并保留未知字段、整份替换、字段校验、写盘后按热更新规则应用。</summary>
public sealed class ConfigSetTests
{
    [Fact]
    public void Edit_PatchMergesAndKeepsUnknownKeys()
    {
        using var dir = new TempDir();
        WriteConfig(dir.Path, extra: """ "extra": { "future": true },""");

        var edited = WorkspaceConfigEditor.Edit(dir.Path, PatchOf("""{ "shell": { "timeoutSeconds": 30 } }"""));

        Assert.Equal(30, edited.Config.Shell!.TimeoutSeconds);
        Assert.Equal(200_000, edited.Config.Shell.MaxOutputBytes);
        Assert.True(edited.Document["extra"]!["future"]!.GetValue<bool>());
        Assert.Equal(dir.Path, edited.Document["root"]!.GetValue<string>());
    }

    [Fact]
    public void Edit_PatchNullRemovesOptionalSection()
    {
        using var dir = new TempDir();
        WriteConfig(dir.Path, extra: """ "notify": { "provider": "none" },""");

        var edited = WorkspaceConfigEditor.Edit(dir.Path, PatchOf("""{ "notify": null }"""));

        Assert.Null(edited.Config.Notify);
        Assert.False(edited.Document.ContainsKey("notify"));
    }

    [Fact]
    public void Edit_PatchCannotChangeRoot()
    {
        using var dir = new TempDir();
        WriteConfig(dir.Path);

        var edited = WorkspaceConfigEditor.Edit(
            dir.Path,
            PatchOf("""{ "root": "/somewhere/else", "shell": { "timeoutSeconds": 10 } }"""));

        Assert.Equal(dir.Path, edited.Config.Root);
        Assert.Equal(dir.Path, edited.Document["root"]!.GetValue<string>());
    }

    [Fact]
    public void Edit_RawReplacesWholeDocument()
    {
        using var dir = new TempDir();
        WriteConfig(dir.Path, extra: """ "extra": { "future": true },""");

        var raw = MinimalJson.Replace("\"models\"", "\"shell\": { \"timeoutSeconds\": 15 }, \"models\"");
        var edited = WorkspaceConfigEditor.Edit(dir.Path, new ConfigSetRequest(Raw: raw));

        Assert.Equal(15, edited.Config.Shell!.TimeoutSeconds);
        Assert.False(edited.Document.ContainsKey("extra"));
    }

    [Fact]
    public void Edit_RequiresExactlyOneOfPatchAndRaw()
    {
        using var dir = new TempDir();
        WriteConfig(dir.Path);

        var neither = Assert.Throws<RpcException>(() => WorkspaceConfigEditor.Edit(dir.Path, new ConfigSetRequest()));
        var both = Assert.Throws<RpcException>(() => WorkspaceConfigEditor.Edit(
            dir.Path,
            new ConfigSetRequest(PatchOf("""{ "shell": {} }""").Patch, Raw: "{}")));

        Assert.Equal(RpcErrors.InvalidParams, neither.Code);
        Assert.Equal(RpcErrors.InvalidParams, both.Code);
    }

    [Fact]
    public void Edit_RawMalformedJson_IsInvalidParams()
    {
        using var dir = new TempDir();
        WriteConfig(dir.Path);

        var ex = Assert.Throws<RpcException>(() =>
            WorkspaceConfigEditor.Edit(dir.Path, new ConfigSetRequest(Raw: "{ not json")));

        Assert.Equal(RpcErrors.InvalidParams, ex.Code);
    }

    [Fact]
    public void Edit_PlaintextSecret_IsRejected()
    {
        using var dir = new TempDir();
        WriteConfig(dir.Path);

        var ex = Assert.Throws<RpcException>(() => WorkspaceConfigEditor.Edit(
            dir.Path,
            PatchOf("""{ "models": { "main": { "apiKeyEnv": "sk-secret-value" } } }""")));

        Assert.Equal(RpcErrors.InvalidParams, ex.Code);
        Assert.Contains("明文", ex.Message);
    }

    [Fact]
    public void Edit_InvalidHeartbeatRange_IsRejected()
    {
        using var dir = new TempDir();
        WriteConfig(dir.Path);

        var ex = Assert.Throws<RpcException>(() => WorkspaceConfigEditor.Edit(
            dir.Path,
            PatchOf("""{ "proactive": { "heartbeat": { "minMinutes": 90, "maxMinutes": 60 } } }""")));

        Assert.Equal(RpcErrors.InvalidParams, ex.Code);
        Assert.Contains("maxMinutes", ex.Message);
    }

    [Fact]
    public void Edit_CompactionBelow_500K_IsRejected()
    {
        using var dir = new TempDir();
        WriteConfig(dir.Path);

        var ex = Assert.Throws<RpcException>(() => WorkspaceConfigEditor.Edit(
            dir.Path,
            PatchOf("""{ "context": { "compaction": { "targetTokens": 400000 } } }""")));

        Assert.Equal(RpcErrors.InvalidParams, ex.Code);
        Assert.Contains("targetTokens", ex.Message);
    }

    [Fact]
    public void Edit_SubagentWithShell_ProducesWarning()
    {
        using var dir = new TempDir();
        WriteConfig(dir.Path);

        var edited = WorkspaceConfigEditor.Edit(dir.Path, PatchOf("""
            {
              "tools": {
                "subagents": {
                  "agents": [
                    {
                      "name": "researcher",
                      "description": "查资料",
                      "instructions": "只做检索",
                      "allowShell": true
                    }
                  ]
                }
              }
            }
            """));

        Assert.Contains(edited.Warnings, warning => warning.Contains("allowShell"));
    }

    [Fact]
    public async Task Set_WritesFileAppliesHotKeysAndReportsRestart()
    {
        using var dir = new TempDir();
        WriteConfig(dir.Path);
        var store = new WorkspaceConfigStore(WorkspaceConfigLoader.Load(dir.Path));
        var reloader = new ConfigReloader(store, new ToolsetBuilder(store.Current), NullLogger<ConfigReloader>.Instance);

        var result = await reloader.SetAsync(PatchOf("""
            {
              "tools": { "deniedPaths": ["private"] },
              "shell": { "denyPatterns": ["shutdown"] }
            }
            """));

        Assert.Equal(["tools.deniedPaths", "shell.denyPatterns"], result.Changed);
        Assert.Empty(result.RestartRequired);
        Assert.Equal(["private"], store.Current.Tools.DeniedPaths);
        Assert.Equal(["shutdown"], store.Current.Shell!.DenyPatterns);

        var written = WorkspaceConfigLoader.Load(dir.Path);
        Assert.Equal(["private"], written.Tools.DeniedPaths);
        Assert.True(File.Exists(Path.Combine(dir.Path, "config.json.bak")));
    }

    [Fact]
    public async Task Set_ModelChange_RequiresRestartButWritesFile()
    {
        using var dir = new TempDir();
        WriteConfig(dir.Path);
        var store = new WorkspaceConfigStore(WorkspaceConfigLoader.Load(dir.Path));
        var reloader = new ConfigReloader(store, new ToolsetBuilder(store.Current), NullLogger<ConfigReloader>.Instance);

        var result = await reloader.SetAsync(
            PatchOf("""{ "models": { "main": { "modelId": "other" } } }"""));

        Assert.Empty(result.Changed);
        Assert.Equal(["models"], result.RestartRequired);
        Assert.Equal("other", store.Current.Models.Main.ModelId);
    }

    [Fact]
    public async Task Set_InvalidConfig_LeavesFileUntouched()
    {
        using var dir = new TempDir();
        WriteConfig(dir.Path);
        var before = File.ReadAllText(Path.Combine(dir.Path, "config.json"));
        var store = new WorkspaceConfigStore(WorkspaceConfigLoader.Load(dir.Path));
        var reloader = new ConfigReloader(store, new ToolsetBuilder(store.Current), NullLogger<ConfigReloader>.Instance);

        await Assert.ThrowsAsync<RpcException>(() =>
            reloader.SetAsync(PatchOf("""{ "shell": { "timeoutSeconds": 0 } }""")));

        Assert.Equal(before, File.ReadAllText(Path.Combine(dir.Path, "config.json")));
    }

    private static ConfigSetRequest PatchOf(string json)
        => new(JsonNode.Parse(json)!.AsObject());

    private const string MinimalJson = """
        {
          "models": {
            "main": { "baseUrl": "http://127.0.0.1:1/v1", "modelId": "main", "apiKeyEnv": "MAIN_KEY" },
            "small": { "baseUrl": "http://127.0.0.1:1/v1", "modelId": "small", "apiKeyEnv": "SMALL_KEY" }
          },
          "channels": {},
          "proactive": { "heartbeat": { "enabled": false } },
          "tools": {}
        }
        """;

    private static void WriteConfig(string root, string extra = "")
        => File.WriteAllText(
            Path.Combine(root, "config.json"),
            MinimalJson.Replace("\"models\"", extra + " \"models\""));
}
