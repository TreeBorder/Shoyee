using System.Text.Json;
using Shoyee.Contracts.Dtos;
using Shoyee.Desktop.Rpc;
using Shoyee.Desktop.ViewModels.Settings;
using Xunit;

#pragma warning disable xUnit1031 // 同步等待：桌面测试共用单 UI 线程

namespace Shoyee.Desktop.Tests;

/// <summary>设置页只读接数：配置原文、工作区路径、工具面与 MCP 状态。</summary>
public sealed class SettingsRuntimeViewModelTests
{
    [Fact]
    public void StartsWithPlaceholderData()
    {
        var runtime = new SettingsRuntimeViewModel();

        Assert.False(runtime.IsLive);
        Assert.True(runtime.IsPlaceholder);
        Assert.NotEmpty(runtime.Tools);
        Assert.Contains("未连接", runtime.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadsConfigPathsToolsAndMcp()
    {
        var gateway = new FakeSettingsGateway
        {
            Config = JsonDocument.Parse("""
                { "models": { "main": { "modelId": "deepseek-flash" } }, "tools": { "autoApproveReadOnly": true } }
                """).RootElement,
            Paths = new WorkspacePathsDto(
                "/tmp/shoyee",
                "/tmp/shoyee/config.json",
                new PromptPathsDto(
                    "/ws/AGENTS.shared.md",
                    "/ws/SOUL.md",
                    "/ws/USER.md",
                    "/ws/MEMORY.md",
                    "/ws/modes/roleplay/AGENTS.md",
                    "/ws/modes/roleplay/WORLD.md",
                    "/ws/modes/work/AGENTS.md"),
                new HeartbeatPathsDto("/ws/.heartbeat/prompts.json", "/ws/.heartbeat/heartbeat.log"),
                "/ws/logs/daemon.log",
                "/ws/exports",
                "/tmp/avatars"),
            Tools =
            [
                new ToolInfo("terminal", "跑命令", "work", true, true, true, "builtin"),
                new ToolInfo("squad_list", "列员工", "all", true, false, false, "mcp:squad"),
            ],
            Mcp =
            [
                new McpServerStatus("squad", "https://squad.example", true, true, ["squad_list"], null),
                new McpServerStatus("rook", "https://rook.example", false, false, [], null),
            ],
        };

        var runtime = new SettingsRuntimeViewModel();
        runtime.AttachGateway(gateway);

        Assert.True(runtime.IsLive);
        Assert.False(runtime.IsPlaceholder);
        Assert.Contains("已连接", runtime.StatusText, StringComparison.Ordinal);
        Assert.Equal("/tmp/shoyee/config.json", runtime.ConfigPath);
        Assert.Contains("\"models\"", runtime.ConfigJson, StringComparison.Ordinal);
        Assert.Contains("\n", runtime.ConfigJson, StringComparison.Ordinal);

        Assert.Equal(2, runtime.Tools.Count);
        var terminal = runtime.Tools.Single(tool => tool.Name == "terminal");
        Assert.True(terminal.RequiresApproval);
        Assert.True(terminal.Writes);
        Assert.Equal("工作", terminal.ScopeLabel);
        var squad = runtime.Tools.Single(tool => tool.Name == "squad_list");
        Assert.True(squad.IsMcp);
        Assert.Equal("squad", squad.Group);

        Assert.Equal(2, runtime.McpServers.Count);
        Assert.Equal("已连接", runtime.McpServers[0].Status);
        Assert.Equal("1 个工具", runtime.McpServers[0].ToolsText);
        Assert.True(runtime.McpServers[0].IsHealthy);
        Assert.Equal("已停用", runtime.McpServers[1].Status);
        Assert.False(runtime.McpServers[1].IsHealthy);

        Assert.Contains(runtime.WorkspacePaths, item => item.Label == "SOUL.md");
        Assert.Contains(runtime.WorkspacePaths, item => item.Label == "daemon 日志");
    }

    [Fact]
    public void KeepsPlaceholderWhenLoadFails()
    {
        var runtime = new SettingsRuntimeViewModel();
        runtime.AttachGateway(new FakeSettingsGateway { Fail = true });

        Assert.False(runtime.IsLive);
        Assert.True(runtime.HasError);
        Assert.Contains("设置数据载入失败", runtime.ErrorText, StringComparison.Ordinal);
        Assert.NotEmpty(runtime.Tools);
    }

    [Fact]
    public void AppliesLiveConfigToPlannedPages()
    {
        var logPath = Path.Combine(Path.GetTempPath(), $"shoyee-log-{Guid.NewGuid():N}.log");
        var promptsPath = Path.Combine(Path.GetTempPath(), $"shoyee-prompts-{Guid.NewGuid():N}.json");
        File.WriteAllLines(logPath, ["boot", "lanes ready"]);
        File.WriteAllText(promptsPath, """
            {"Text":{"l1":"轻轻问候"},"Voice":{"l1":"说一句晚安"}}
            """);
        try
        {
            var gateway = new FakeSettingsGateway
            {
                Config = JsonDocument.Parse("""
                    {
                      "tools": { "autoApproveReadOnly": true, "mcp": [{ "name": "squad", "url": "https://squad.example", "enabled": true }] },
                      "voice": { "provider": "mimo", "providers": { "mimo": { "baseUrl": "https://api.mimo.example/v1", "apiKeyEnv": "XIAOMI_API_KEY" } } },
                      "proactive": { "heartbeat": { "enabled": true, "minMinutes": 45, "maxMinutes": 75, "hotZoneMinutes": 20, "cooldownMinutes": 40, "longingCap": 100, "triggerPenalty": 50, "levelThresholds": [15, 30, 50, 70] } },
                      "shell": { "workingDirectory": "/ws/Workspace", "timeoutSeconds": 60 }
                    }
                    """).RootElement,
                Paths = new WorkspacePathsDto(
                    "/ws",
                    "/ws/config.json",
                    new PromptPathsDto("/ws/a", "/ws/b", "/ws/c", "/ws/d", "/ws/e", "/ws/f", "/ws/g"),
                    new HeartbeatPathsDto(promptsPath, "/ws/.heartbeat/heartbeat.log"),
                    logPath,
                    "/ws/exports",
                    "/tmp/avatars"),
                Status = new StatusDto(
                    "test",
                    DateTimeOffset.Now,
                    1,
                    "/ws",
                    [],
                    new UsageSummary(0, 0, 0, 0, 0),
                    [new StateDto("mood", "心情", "平静", "当前心情", true, DateTimeOffset.Now)],
                    new DbInfo("/ws/state.db", 0),
                    null),
                Mcp =
                [
                    new McpServerStatus("squad", "https://squad.example", true, true, ["squad_list"], null),
                ],
            };

            var runtime = new SettingsRuntimeViewModel();
            runtime.AttachGateway(gateway);
            Assert.True(runtime.IsLive);

            var pages = new List<SettingsPageViewModel>
            {
                Page("tools.approval", new SettingsGroupItem("审批", "", [
                    new SettingsFieldItem("只读免审批", "false", SettingsFieldKind.Switch),
                ])),
                Page("providers.voice", new SettingsGroupItem("语音", "", [
                    new SettingsFieldItem("provider", "none"),
                    new SettingsFieldItem("Mimo base_url", ""),
                ])),
                Page("providers.runtime", new SettingsGroupItem("Shell", "", [
                    new SettingsFieldItem("超时（秒）", "0", SettingsFieldKind.Number),
                ])),
                Page("config.heartbeat"),
                Page("roleplay.heartbeat"),
                Page("roleplay.states"),
                Page("config.logs"),
                Page("mcp.tools"),
            };
            SettingsLiveConfig.Apply(pages, runtime);

            Assert.Equal("true", FieldValue(pages, "tools.approval", "审批", "只读免审批"));
            Assert.Equal("mimo", FieldValue(pages, "providers.voice", "语音", "provider"));
            Assert.Equal(
                "https://api.mimo.example/v1",
                FieldValue(pages, "providers.voice", "语音", "Mimo base_url"));
            Assert.Equal("60", FieldValue(pages, "providers.runtime", "Shell", "超时（秒）"));
            Assert.Equal("45 / 75", FieldValue(pages, "config.heartbeat", "生效值", "Min / Max 分钟"));
            Assert.Equal("轻轻问候", FieldValue(pages, "roleplay.heartbeat", "Text", "l1"));
            Assert.Equal("平静", FieldValue(pages, "roleplay.states", "当前状态", "心情（mood）"));
            Assert.Contains("lanes ready", FieldValue(pages, "config.logs", "尾部", "最近输出")!, StringComparison.Ordinal);
            Assert.Equal("已启用", FieldValue(pages, "mcp.tools", "squad", "squad_list"));
            Assert.True(pages.Single(page => page.Key == "providers.voice").IsLiveData);
        }
        finally
        {
            File.Delete(logPath);
            File.Delete(promptsPath);
        }
    }

    [Fact]
    public void SavesChangedStatesAndReloadsConfig()
    {
        var gateway = new FakeSettingsGateway
        {
            Status = new StatusDto(
                "test",
                DateTimeOffset.Now,
                1,
                "/ws",
                [],
                new UsageSummary(0, 0, 0, 0, 0),
                [
                    new StateDto("mood", "心情", "平静", "当前心情", true, DateTimeOffset.Now),
                    new StateDto("longing", "思念", "69", null, true, DateTimeOffset.Now),
                ],
                new DbInfo("/ws/state.db", 0),
                null),
        };

        var runtime = new SettingsRuntimeViewModel();
        runtime.AttachGateway(gateway);

        var mood = runtime.StateRows.Single(state => state.Key == "mood");
        mood.Value = "愉快";
        Assert.True(mood.IsDirty);

        runtime.SaveStatesCommand.Execute(null);

        Assert.Equal(("mood", "愉快"), Assert.Single(gateway.StateSets));
        Assert.False(mood.IsDirty);
        Assert.Contains("已保存 1 个状态", runtime.StatusText, StringComparison.Ordinal);

        runtime.ReloadConfigCommand.Execute(null);
        Assert.Equal(1, gateway.ReloadCalls);
        Assert.Contains("配置已重载", runtime.StatusText, StringComparison.Ordinal);

        runtime.ReloadMcpCommand.Execute(null);
        Assert.Contains("MCP 已重连", runtime.StatusText, StringComparison.Ordinal);

        runtime.RequestRestartCommand.Execute(null);
        Assert.True(runtime.IsConfirmingRestart);
        runtime.ConfirmRestartCommand.Execute(null);
        Assert.Equal(1, gateway.RestartCalls);
        Assert.Contains("已请求重启", runtime.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void SavesEditedConfigFieldsAsPatch()
    {
        var gateway = new FakeSettingsGateway();
        var runtime = new SettingsRuntimeViewModel();
        runtime.AttachGateway(gateway);

        var page = new SettingsPageViewModel(
            new SettingsPageItem(
                "providers.voice",
                "语音",
                "",
                "",
                SettingsPageStatus.Planned,
                [],
                [
                    new SettingsGroupItem("语音", "", [
                        new SettingsFieldItem("provider", "mimo", configPath: "voice.provider"),
                        new SettingsFieldItem("Mimo base_url", "https://old", configPath: "voice.providers.mimo.baseUrl"),
                    ]),
                ]),
            _ => { })
        {
            IsLiveData = true,
        };

        page.BeginEdit();
        var provider = page.Groups!.SelectMany(group => group.Fields).Single(field => field.Label == "provider");
        provider.Value = "none";
        Assert.True(provider.IsDirty);

        runtime.SaveSettingsPageAsync(page).GetAwaiter().GetResult();

        var patch = Assert.Single(gateway.ConfigSets);
        Assert.Equal("none", patch["voice"]!["provider"]!.GetValue<string>());
        Assert.False(provider.IsDirty);
        Assert.False(page.IsEditing);
        Assert.Contains("已保存 1 个字段", runtime.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadsPromptFilesIndexAndSubagents()
    {
        var soulPath = Path.Combine(Path.GetTempPath(), $"shoyee-soul-{Guid.NewGuid():N}.md");
        File.WriteAllText(soulPath, "# 人设\n\n她叫筱义。");
        try
        {
            var gateway = new FakeSettingsGateway
            {
                Config = JsonDocument.Parse("""
                    {
                      "tools": {
                        "subagents": {
                          "autoWait": false,
                          "waitTimeoutSeconds": 120,
                          "maxLoopIterations": 5,
                          "agents": [
                            { "name": "yueyue", "description": "前端", "instructions": "做页面", "allowShell": false, "tools": ["file_read"] }
                          ]
                        }
                      }
                    }
                    """).RootElement,
                Paths = new WorkspacePathsDto(
                    "/ws",
                    "/ws/config.json",
                    new PromptPathsDto("/ws/a", soulPath, "/ws/c", "/ws/d", "/ws/e", "/ws/f", "/ws/g"),
                    new HeartbeatPathsDto("/ws/p", "/ws/l"),
                    "/ws/log",
                    "/ws/out",
                    "/ws/av"),
            };
            gateway.Index.Add(new IndexEntry(
                1,
                "ems",
                "EMS 权限库",
                "note",
                "Akasha/EMS.md",
                "work",
                ["运维"],
                "表结构",
                Pinned: false,
                DateTimeOffset.Now,
                DateTimeOffset.Now));

            var runtime = new SettingsRuntimeViewModel();
            runtime.AttachGateway(gateway);

            var soul = runtime.GetPrompt("prompts.soul");
            Assert.NotNull(soul);
            Assert.Contains("她叫筱义", soul!.Preview, StringComparison.Ordinal);
            Assert.True(soul.HasFile);
            Assert.Equal(soulPath, soul.Path);

            var index = Assert.Single(runtime.IndexRows);
            Assert.Equal("EMS 权限库", index.Name);
            Assert.True(index.IsEnabled);

            runtime.ToggleIndexCommand.Execute(index);
            Assert.False(index.IsEnabled);

            runtime.DeleteIndexCommand.Execute(index);
            Assert.Empty(runtime.IndexRows);

            var subagent = Assert.Single(runtime.Subagents);
            Assert.Equal("yueyue", subagent.Name);
            Assert.Equal("前端", subagent.Description);
            Assert.Equal("120", runtime.SubagentWaitTimeout);
            Assert.Equal("5", runtime.SubagentMaxLoops);
            Assert.False(runtime.SubagentAutoWaitEnabled);
        }
        finally
        {
            File.Delete(soulPath);
        }
    }

    [Fact]
    public void SavesHeartbeatConfigAndPromptsFile()
    {
        var promptsPath = Path.Combine(Path.GetTempPath(), $"shoyee-hb-{Guid.NewGuid():N}.json");
        File.WriteAllText(promptsPath, """
            {"Text":{"l1":"旧问候"},"Voice":{"l1":"旧语音"}}
            """);
        try
        {
            var gateway = new FakeSettingsGateway
            {
                Config = JsonDocument.Parse("""
                    { "proactive": { "heartbeat": { "enabled": true, "minMinutes": 45, "maxMinutes": 75 } } }
                    """).RootElement,
                Paths = new WorkspacePathsDto(
                    "/ws",
                    "/ws/config.json",
                    new PromptPathsDto("/ws/a", "/ws/b", "/ws/c", "/ws/d", "/ws/e", "/ws/f", "/ws/g"),
                    new HeartbeatPathsDto(promptsPath, "/ws/.heartbeat/heartbeat.log"),
                    "/ws/log",
                    "/ws/out",
                    "/ws/av"),
            };

            var runtime = new SettingsRuntimeViewModel();
            runtime.AttachGateway(gateway);

            Assert.True(runtime.HeartbeatEnabled);
            Assert.Equal(2, runtime.HeartbeatPrompts.Count);
            runtime.HeartbeatMinMinutes = "30";
            runtime.HeartbeatPrompts[0].Lines[0].Text = "新问候";

            runtime.SaveHeartbeatCommand.Execute(null);

            var patch = Assert.Single(gateway.ConfigSets);
            Assert.Equal(30, patch["proactive"]!["heartbeat"]!["minMinutes"]!.GetValue<int>());
            Assert.Contains("新问候", File.ReadAllText(promptsPath), StringComparison.Ordinal);
            Assert.Contains("心跳已保存", runtime.StatusText, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(promptsPath);
        }
    }

    private static SettingsPageViewModel Page(string key, params SettingsGroupItem[] groups)
        => new(
            new SettingsPageItem(
                key,
                key,
                "",
                "",
                SettingsPageStatus.Planned,
                [],
                groups.Length == 0 ? null : groups),
            _ => { });

    private static string? FieldValue(
        IEnumerable<SettingsPageViewModel> pages,
        string pageKey,
        string group,
        string label)
        => pages.Single(page => page.Key == pageKey).Groups!
            .Single(item => item.Title == group)
            .Fields.Single(field => field.Label == label)
            .Value;

    private sealed class FakeSettingsGateway : ISettingsGateway
    {
        public bool IsConnected { get; set; } = true;

        public bool Fail { get; set; }

        public JsonElement Config { get; set; }

        public WorkspacePathsDto Paths { get; set; } = new(
            "/ws",
            "/ws/config.json",
            new PromptPathsDto("/ws/a", "/ws/b", "/ws/c", "/ws/d", "/ws/e", "/ws/f", "/ws/g"),
            new HeartbeatPathsDto("/ws/p", "/ws/l"),
            "/ws/log",
            "/ws/out",
            "/ws/av");

        public IReadOnlyList<ToolInfo> Tools { get; set; } = [];

        public IReadOnlyList<McpServerStatus> Mcp { get; set; } = [];

        public StatusDto? Status { get; set; }

        public List<(string Key, string Value)> StateSets { get; } = [];

        public List<System.Text.Json.Nodes.JsonObject> ConfigSets { get; } = [];

        public int ReloadCalls { get; private set; }

        public Task<JsonElement> GetConfigAsync(CancellationToken cancellationToken = default)
            => Fail
                ? throw new DaemonUnavailableException("daemon 未连接")
                : Task.FromResult(Config);

        public Task<WorkspacePathsDto> GetWorkspacePathsAsync(CancellationToken cancellationToken = default)
            => Fail
                ? throw new DaemonUnavailableException("daemon 未连接")
                : Task.FromResult(Paths);

        public Task<IReadOnlyList<ToolInfo>> ListToolsAsync(
            string? scope = null,
            string? query = null,
            CancellationToken cancellationToken = default)
            => Fail
                ? throw new DaemonUnavailableException("daemon 未连接")
                : Task.FromResult(Tools);

        public Task<IReadOnlyList<McpServerStatus>> GetMcpStatusAsync(
            CancellationToken cancellationToken = default)
            => Fail
                ? throw new DaemonUnavailableException("daemon 未连接")
                : Task.FromResult(Mcp);

        public Task<StatusDto> GetStatusAsync(CancellationToken cancellationToken = default)
            => Fail
                ? throw new DaemonUnavailableException("daemon 未连接")
                : Task.FromResult(Status ?? new StatusDto(
                    "test",
                    DateTimeOffset.Now,
                    1,
                    "/ws",
                    [],
                    new UsageSummary(0, 0, 0, 0, 0),
                    [],
                    new DbInfo("/ws/state.db", 0),
                    null));

        public Task<StateDto> SetStateAsync(
            string key,
            string value,
            CancellationToken cancellationToken = default)
        {
            StateSets.Add((key, value));
            return Task.FromResult(new StateDto(key, key, value, null, true, DateTimeOffset.Now));
        }

        public Task<ConfigReloadResult> ReloadConfigAsync(CancellationToken cancellationToken = default)
        {
            ReloadCalls++;
            return Task.FromResult(new ConfigReloadResult(["proactive"], []));
        }

        public Task<ConfigSetResult> SetConfigAsync(
            System.Text.Json.Nodes.JsonObject patch,
            CancellationToken cancellationToken = default)
        {
            ConfigSets.Add(patch);
            return Task.FromResult(new ConfigSetResult(["voice"], [], "/ws/config.json", []));
        }

        public Task<McpReloadResult> ReloadMcpAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new McpReloadResult(Mcp, RestartRequired: false));

        public List<IndexEntry> Index { get; } = [];

        public int RestartCalls { get; private set; }

        public Task RestartDaemonAsync(CancellationToken cancellationToken = default)
        {
            RestartCalls++;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<IndexEntry>> ListIndexAsync(
            string? scope = null,
            string? query = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<IndexEntry>>(Index);

        public Task<IndexEntry> UpsertIndexAsync(
            IndexEntry entry,
            CancellationToken cancellationToken = default)
        {
            Index.RemoveAll(item => item.Key == entry.Key);
            Index.Add(entry);
            return Task.FromResult(entry);
        }

        public Task RemoveIndexAsync(string key, CancellationToken cancellationToken = default)
        {
            Index.RemoveAll(item => item.Key == key);
            return Task.CompletedTask;
        }
    }
}
