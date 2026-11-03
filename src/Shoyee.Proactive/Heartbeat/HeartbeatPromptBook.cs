using System.Text.Json;

namespace Shoyee.Proactive.Heartbeat;

/// <summary>
/// 心跳提示词（<c>{workspace}/.heartbeat/prompts.json</c>）。
///
/// 形状照 Harem：<c>{ "Text": { "l1": "…", … }, "Story": { … } }</c>；
/// 每次触发时重读文件，改完下一次生效（与 RP 的人设文件同一套习惯）。
/// 文件缺失/格式错/对应档位为空 → 返回 null，调用方用通用提示兜底，不拦心跳。
/// </summary>
public sealed class HeartbeatPromptBook
{
    /// <summary>工作区里的相对路径。</summary>
    public const string RelativePath = ".heartbeat/prompts.json";

    private readonly string _workspaceRoot;

    /// <summary>创建工作区提示词读取器。</summary>
    /// <param name="workspaceRoot">工作区根目录。</param>
    public HeartbeatPromptBook(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _workspaceRoot = workspaceRoot;
    }

    /// <summary>取某个类别/档位的提示词（档位 0 起，映射到 l1–l5）。</summary>
    /// <param name="type">类别（text / story）。</param>
    /// <param name="level">档位（0 起）。</param>
    public string? Resolve(string type, int level)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return null;
        }

        var prompts = Load();
        if (!prompts.TryGetValue(type, out var levels))
        {
            return null;
        }

        var key = "l" + (Math.Max(0, level) + 1);
        return levels.TryGetValue(key, out var text) && !string.IsNullOrWhiteSpace(text)
            ? text
            : null;
    }

    private Dictionary<string, Dictionary<string, string>> Load()
    {
        var path = Path.Combine(_workspaceRoot, RelativePath);
        try
        {
            if (!File.Exists(path))
            {
                return [];
            }

            var json = File.ReadAllText(path);
            var parsed = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (parsed is null)
            {
                return [];
            }

            // 字典键不受 PropertyNameCaseInsensitive 影响，这里自己折成大小写不敏感
            return parsed.ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }
}
