using Xunit;
using Shoyee.Daemon.Configuration;
using Shoyee.Daemon.Tests.TestSupport;

namespace Shoyee.Daemon.Tests.Configuration;

public sealed class WorkspaceConfigLoaderTests
{
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

    [Fact]
    public void Load_OverridesRootWithWorkspace()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "config.json"), MinimalJson);

        var config = WorkspaceConfigLoader.Load(dir.Path);

        Assert.Equal(dir.Path, config.Root);
        Assert.Equal("main", config.Models.Main.ModelId);
        Assert.Equal("MAIN_KEY", config.Models.Main.ApiKeyEnv);
        Assert.False(config.Proactive.Heartbeat.Enabled);
    }

    [Fact]
    public void Load_FileRootIsIgnored()
    {
        using var dir = new TempDir();
        var json = MinimalJson.Replace("\"models\"", "\"root\": \"/somewhere/else\", \"models\"");
        File.WriteAllText(Path.Combine(dir.Path, "config.json"), json);

        var config = WorkspaceConfigLoader.Load(dir.Path);

        Assert.Equal(dir.Path, config.Root);
    }

    [Fact]
    public void MissingConfig_Throws()
    {
        using var dir = new TempDir();

        var ex = Assert.Throws<DaemonStartupException>(() => WorkspaceConfigLoader.Load(dir.Path));

        Assert.Contains("config.json", ex.Message);
    }

    [Fact]
    public void MalformedJson_Throws()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "config.json"), "{ not json");

        Assert.Throws<DaemonStartupException>(() => WorkspaceConfigLoader.Load(dir.Path));
    }

    [Fact]
    public void IncompleteConfig_Throws()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "config.json"), """{ "channels": {}, "proactive": {}, "tools": {} }""");

        Assert.Throws<DaemonStartupException>(() => WorkspaceConfigLoader.Load(dir.Path));
    }
}
