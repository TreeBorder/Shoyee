using System.Text.Json;
using Shoyee.Desktop.ViewModels.Items;

namespace Shoyee.Desktop.ViewModels.Settings;

/// <summary>
/// 把 daemon 的当前生效配置写回设置页的表单（只读）。
///
/// 目录里的 <see cref="SettingsPageItem.Groups"/> 只当结构模板：标题、字段标签与说明保留，
/// 值按页面键映射到 config.json / status.get / prompts.json / daemon 日志的实时数据。
/// 没有映射的页（如 roleplay.context 依赖尚未进 config 的键）保持占位。
/// </summary>
public static class SettingsLiveConfig
{
    /// <summary>对全部设置子页应用实时值。</summary>
    /// <param name="pages">设置页列表。</param>
    /// <param name="runtime">已拉到的运行数据。</param>
    public static void Apply(IEnumerable<SettingsPageViewModel> pages, SettingsRuntimeViewModel runtime)
    {
        if (!runtime.IsLive || runtime.Config.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var config = runtime.Config;
        foreach (var page in pages)
        {
            var groups = page.Key switch
            {
                "tools.approval" => Map(page.Definition.Groups, label => label switch
                {
                    "只读免审批" => V(Bool(Text(config, "tools.autoApproveReadOnly")), "tools.autoApproveReadOnly"),
                    "denyPatterns" => V(Lines(config, "shell.denyPatterns"), "shell.denyPatterns", isList: true),
                    "deniedPaths" => V(Lines(config, "tools.deniedPaths"), "tools.deniedPaths", isList: true),
                    _ => null,
                }),
                "providers.voice" => Map(page.Definition.Groups, label => label switch
                {
                    "provider" => V(Text(config, "voice.provider"), "voice.provider"),
                    "合成" => V(
                        Text(config, "voice.synthesisProvider") ?? Text(config, "voice.provider"),
                        "voice.synthesisProvider"),
                    "转写" => V(
                        Text(config, "voice.transcriptionProvider") ?? Text(config, "voice.provider"),
                        "voice.transcriptionProvider"),
                    "Mimo base_url" => V(Text(config, "voice.providers.mimo.baseUrl"), "voice.providers.mimo.baseUrl"),
                    "Mimo key 环境变量" => V(
                        Text(config, "voice.providers.mimo.apiKeyEnv"),
                        "voice.providers.mimo.apiKeyEnv"),
                    _ => null,
                }),
                "providers.image" => Map(page.Definition.Groups, (label, _) => label switch
                {
                    "provider" => V(Text(config, "photo.provider"), "photo.provider"),
                    "ComfyUI 端点" => V(Text(config, "photo.providers.comfy.endpoint"), "photo.providers.comfy.endpoint"),
                    "角色 key" => V(Text(config, "photo.character"), "photo.character"),
                    "出图目录" => V(Path.Combine(runtime.Paths?.Workspace ?? "", "assets/images")),
                    _ => null,
                }),
                "providers.web" => Map(page.Definition.Groups, label => label switch
                {
                    "provider" => V(Text(config, "web.provider"), "web.provider"),
                    "base_url" => V(Text(config, "web.providers.deepseek.baseUrl"), "web.providers.deepseek.baseUrl"),
                    "key 环境变量" => V(
                        Text(config, "web.providers.deepseek.apiKeyEnv"),
                        "web.providers.deepseek.apiKeyEnv"),
                    _ => null,
                }),
                "providers.notes" => Map(page.Definition.Groups, label => label switch
                {
                    "provider" => V(Text(config, "notes.provider"), "notes.provider"),
                    "vaultRoot" => V(Text(config, "notes.providers.obsidian.vaultRoot"), "notes.providers.obsidian.vaultRoot"),
                    "notesDir" => V(Text(config, "notes.providers.obsidian.notesDir"), "notes.providers.obsidian.notesDir"),
                    _ => null,
                }),
                "providers.runtime" => Map(page.Definition.Groups, (label, group) => label switch
                {
                    "工作目录" => V(Text(config, "shell.workingDirectory"), "shell.workingDirectory"),
                    "超时（秒）" when group == "Shell" => V(Text(config, "shell.timeoutSeconds"), "shell.timeoutSeconds"),
                    "超时（秒）" when group == "附件" => V(Text(config, "attachments.timeoutSeconds"), "attachments.timeoutSeconds"),
                    "自动分析" => V(Bool(Text(config, "attachments.autoAnalyze")), "attachments.autoAnalyze"),
                    "图片提示词" => V(Text(config, "attachments.imagePrompt"), "attachments.imagePrompt"),
                    "databasePath" => V(Text(config, "databasePath"), "databasePath"),
                    "databaseProvider" => V(Text(config, "databaseProvider")),
                    _ => null,
                }),
                "config.heartbeat" => Heartbeat(config, runtime, includePrompts: false),
                "roleplay.heartbeat" => Heartbeat(config, runtime, includePrompts: true),
                "roleplay.states" => States(runtime),
                "config.logs" => Logs(runtime),
                "mcp.tools" => McpTools(runtime),
                _ => null,
            };

            if (groups is not null)
            {
                page.Groups = groups;
                page.IsLiveData = true;
            }
        }
    }

    private static IReadOnlyList<SettingsGroupItem> Map(
        IReadOnlyList<SettingsGroupItem>? groups,
        Func<string, LiveValue?> resolve)
        => Map(groups, (label, _) => resolve(label));

    private static IReadOnlyList<SettingsGroupItem> Map(
        IReadOnlyList<SettingsGroupItem>? groups,
        Func<string, string, LiveValue?> resolve)
    {
        if (groups is null)
        {
            return [];
        }

        return groups
            .Select(group => group with
            {
                Fields = group.Fields
                    .Select(field => resolve(field.Label, group.Title) is { } live
                        ? field.WithLive(live.Value, live.Path, live.IsList)
                        : field)
                    .ToList(),
            })
            .ToList();
    }

    private static LiveValue V(string? value, string? path = null, bool isList = false)
        => new(value, path, isList);

    private static IReadOnlyList<SettingsGroupItem> Heartbeat(
        JsonElement config,
        SettingsRuntimeViewModel runtime,
        bool includePrompts)
    {
        var prefix = "proactive.heartbeat";
        var quietStart = Text(config, prefix + ".quietHoursStart") ?? "23:00";
        var quietEnd = Text(config, prefix + ".quietHoursEnd") ?? "06:30";
        var groups = new List<SettingsGroupItem>
        {
            new(
                "生效值",
                "daemon 当前使用的心跳参数（不是代码默认值）。",
                [
                    Field("Enabled", Bool(Text(config, prefix + ".enabled")), SettingsFieldKind.Switch, prefix + ".enabled"),
                    Field("Min / Max 分钟", $"{Text(config, prefix + ".minMinutes")} / {Text(config, prefix + ".maxMinutes")}", SettingsFieldKind.ReadOnly),
                    Field("HotZone / Cooldown", $"{Text(config, prefix + ".hotZoneMinutes")} / {Text(config, prefix + ".cooldownMinutes")}", SettingsFieldKind.ReadOnly),
                    Field("TriggerPenalty", Text(config, prefix + ".triggerPenalty") ?? "", SettingsFieldKind.Number, prefix + ".triggerPenalty"),
                    Field("TriggerThreshold", Text(config, prefix + ".triggerThreshold") ?? "", SettingsFieldKind.Number),
                    Field("LongingCap", Text(config, prefix + ".longingCap") ?? "", SettingsFieldKind.Number, prefix + ".longingCap"),
                    Field("LevelThresholds", Text(config, prefix + ".levelThresholds") ?? "", SettingsFieldKind.Text),
                    Field("静默", $"{quietStart} – {quietEnd}", SettingsFieldKind.ReadOnly),
                ]),
        };

        if (includePrompts)
        {
            groups.AddRange(ReadPrompts(runtime));
        }

        return groups;
    }

    private static IReadOnlyList<SettingsGroupItem> ReadPrompts(SettingsRuntimeViewModel runtime)
    {
        var path = runtime.Paths?.Heartbeat.Prompts;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return [];
            }

            var groups = new List<SettingsGroupItem>();
            foreach (var kind in document.RootElement.EnumerateObject())
            {
                if (kind.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var fields = kind.Value.EnumerateObject()
                    .Select(level => Field(level.Name, level.Value.GetString() ?? "", SettingsFieldKind.Multiline))
                    .ToList();
                groups.Add(new SettingsGroupItem(
                    kind.Name,
                    "系统消息注入：触发时以用户身份交给她的指令。",
                    fields));
            }

            return groups;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static IReadOnlyList<SettingsGroupItem> States(SettingsRuntimeViewModel runtime)
    {
        if (runtime.Status is not { } status)
        {
            return [];
        }

        var fields = status.States
            .Select(state => new SettingsFieldItem(
                $"{state.Name}（{state.Key}）",
                state.Value,
                SettingsFieldKind.ReadOnly,
                state.Description))
            .ToList();

        return
        [
            new SettingsGroupItem(
                "当前状态",
                "角色状态表的当前值；由她自己通过状态工具维护。",
                fields),
        ];
    }

    private static IReadOnlyList<SettingsGroupItem> Logs(SettingsRuntimeViewModel runtime)
    {
        var path = runtime.Paths?.DaemonLog ?? "";
        var tail = string.IsNullOrWhiteSpace(runtime.DaemonLogTail) ? "（没有读到日志内容）" : runtime.DaemonLogTail;
        return
        [
            new SettingsGroupItem(
                "日志文件",
                "daemon 日志路径与尾部内容（本机直读）。",
                [
                    Field("路径", path, SettingsFieldKind.ReadOnly),
                    Field("保留天数", "7", SettingsFieldKind.Number),
                    Field("级别", "information", SettingsFieldKind.Text),
                ]),
            new SettingsGroupItem(
                "尾部",
                "最近 120 行。",
                [Field("最近输出", tail, SettingsFieldKind.Multiline)]),
        ];
    }

    private static IReadOnlyList<SettingsGroupItem> McpTools(SettingsRuntimeViewModel runtime)
        => runtime.McpServers
            .Select(server => new SettingsGroupItem(
                server.Name,
                $"{server.Endpoint} · {server.Status} · {server.ToolsText}",
                server.Tools.Count == 0
                    ? [Field("工具清单", "没有拿到工具（未连接或白名单为空）", SettingsFieldKind.ReadOnly)]
                    : server.Tools
                        .Select(name => Field(name, "已启用", SettingsFieldKind.ReadOnly))
                        .ToList()))
            .ToList();

    private static SettingsFieldItem Field(
        string label,
        string value,
        SettingsFieldKind kind,
        string? configPath = null)
        => new(label, value, kind, configPath);

    private readonly record struct LiveValue(string? Value, string? Path = null, bool IsList = false);

    private static string? Text(JsonElement root, string path)
    {
        var current = root;
        foreach (var part in path.Split('.'))
        {
            if (current.ValueKind != JsonValueKind.Object
                || !current.TryGetProperty(part, out current))
            {
                return null;
            }
        }

        return current.ValueKind switch
        {
            JsonValueKind.String => current.GetString(),
            JsonValueKind.Number => current.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Array => string.Join(" / ", current.EnumerateArray().Select(item => item.ToString())),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => current.GetRawText(),
        };
    }

    private static string Bool(string? value)
        => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ? "true" : "false";

    private static string Lines(JsonElement root, string path)
    {
        var value = Text(root, path);
        return value is null ? "" : value.Replace(" / ", Environment.NewLine, StringComparison.Ordinal);
    }
}
