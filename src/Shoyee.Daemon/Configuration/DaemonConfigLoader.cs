using System.Text.Json.Nodes;
using Shoyee.Client.Transport;

namespace Shoyee.Daemon.Configuration;

/// <summary>daemon 引导配置：<c>~/Library/Application Support/Shoyee/config.json</c>。</summary>
/// <param name="Workspace">工作区根目录（模型/渠道配置与人设文件都在那里）。</param>
public sealed record DaemonConfig(string Workspace);

/// <summary>
/// 读引导配置。工作区不存在时**直接报错**，不回落到本地盘——避免把数据写进不该写的地方。
/// </summary>
public static class DaemonConfigLoader
{
    /// <summary>读取并校验 daemon 配置。</summary>
    /// <param name="paths">路径集合。</param>
    public static DaemonConfig Load(ShoyeePaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (!File.Exists(paths.ConfigPath))
        {
            throw new DaemonStartupException(
                $"缺少 daemon 配置：{paths.ConfigPath}\n"
                + "内容示例：{ \"workspace\": \"~/Shoyee\" }");
        }

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(File.ReadAllText(paths.ConfigPath));
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
        {
            throw new DaemonStartupException($"daemon 配置无法读取：{paths.ConfigPath}（{ex.Message}）", ex);
        }

        var workspace = node is JsonObject root && root["workspace"] is JsonValue value
            ? value.GetValue<string>()
            : null;
        if (string.IsNullOrWhiteSpace(workspace))
        {
            throw new DaemonStartupException($"daemon 配置缺少 workspace：{paths.ConfigPath}");
        }

        var resolved = Path.GetFullPath(ExpandHome(workspace.Trim()));
        if (!Directory.Exists(resolved))
        {
            throw new DaemonStartupException(
                $"工作区不存在：{resolved}\n"
                + $"请检查 {paths.ConfigPath} 或先挂载磁盘；不会自动回落到本地盘。");
        }

        return new DaemonConfig(resolved);
    }

    private static string ExpandHome(string path)
        => path == "~" || path.StartsWith("~/", StringComparison.Ordinal)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                path.Length <= 2 ? string.Empty : path[2..])
            : path;
}
