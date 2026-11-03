using System.Text.Json;

namespace Shoyee.Desktop.Conversations;

/// <summary>从 Harem 的工作区导入角色对话。</summary>
internal static class HaremHistoryImporter
{
    /// <summary>按路径导入；支持工作区根、.history 目录、单个会话目录或 rounds.json。</summary>
    /// <param name="path">Harem 工作区 / .history / 会话目录 / rounds.json。</param>
    /// <returns>解析后的角色时间线。</returns>
    public static RoleplayContent Import(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("请输入 Harem 的路径。", nameof(path));
        }

        var sessionDirectory = ResolveSessionDirectory(path.Trim());
        var roundsPath = Path.Combine(sessionDirectory, "rounds.json");
        if (!File.Exists(roundsPath))
        {
            throw new FileNotFoundException($"这个会话目录里没有 rounds.json：{sessionDirectory}");
        }

        var workspace = Directory.GetParent(sessionDirectory)?.Parent?.FullName;
        List<RoleplayRawState> state = workspace is null
            ? []
            : ReadState(Path.Combine(workspace, "state.json"));
        var agentName = workspace is null
            ? "筱义"
            : ReadAgentName(Path.Combine(workspace, "config.json"));

        using var document = JsonDocument.Parse(File.ReadAllText(roundsPath));
        var rounds = document.RootElement.ValueKind == JsonValueKind.Array
            ? document.RootElement.EnumerateArray().Select(item => item.Clone()).ToList()
            : [];

        return HaremTranscriptReader.Read(new RoleplayRawFile
        {
            AgentName = agentName,
            State = state,
            Rounds = rounds,
        });
    }

    private static string ResolveSessionDirectory(string path)
    {
        if (File.Exists(path))
        {
            if (Path.GetFileName(path).Equals("rounds.json", StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetDirectoryName(path)!;
            }

            throw new FileNotFoundException($"请选择 Harem 工作区、.history 目录或 rounds.json：{path}");
        }

        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException($"目录不存在：{path}");
        }

        if (File.Exists(Path.Combine(path, "rounds.json")))
        {
            return path;
        }

        var history = Path.Combine(path, ".history");
        if (!Directory.Exists(history))
        {
            history = path;
            if (!Path.GetFileName(path).Equals(".history", StringComparison.OrdinalIgnoreCase))
            {
                throw new DirectoryNotFoundException($"这个目录里没有 .history：{path}");
            }
        }

        var latest = Directory.GetDirectories(history)
            .Select(directory => (Directory: directory, File: Path.Combine(directory, "rounds.json")))
            .Where(item => File.Exists(item.File))
            .OrderByDescending(item => File.GetLastWriteTimeUtc(item.File))
            .FirstOrDefault();

        if (latest.Directory is null)
        {
            throw new FileNotFoundException($"这个目录下没有可导入的会话：{history}");
        }

        return latest.Directory;
    }

    private static List<RoleplayRawState> ReadState(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        return JsonSerializer.Deserialize<List<RoleplayRawState>>(
            File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
    }

    private static string ReadAgentName(string path)
    {
        if (!File.Exists(path))
        {
            return "筱义";
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.TryGetProperty("Agent", out var agent)
            && agent.TryGetProperty("Name", out var name)
            && name.ValueKind == JsonValueKind.String
            ? name.GetString() ?? "筱义"
            : "筱义";
    }
}
