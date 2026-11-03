using System.Text.Json;
using System.Text.Json.Nodes;
using Shoyee.Contracts.Options;
using Shoyee.Contracts.Rpc;

namespace Shoyee.Daemon.Configuration;

/// <summary>
/// 读工作区配置 <c>{workspace}/config.json</c>。文件里的 <c>root</c> 以 daemon 启动参数为准
/// （写进来会被覆盖），避免配置里同时也写死一个会漂移的路径。
/// </summary>
public static class WorkspaceConfigLoader
{
    /// <summary>读取并校验工作区配置。</summary>
    /// <param name="workspaceRoot">工作区根目录（已存在）。</param>
    public static WorkspaceConfig Load(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        return FromDocument(ReadDocument(Path.Combine(workspaceRoot, "config.json")), workspaceRoot);
    }

    /// <summary>把配置文件读成 JSON 文档（<c>config.set</c> 的局部合并也基于它，保留未知字段）。</summary>
    /// <param name="path">config.json 路径。</param>
    public static JsonObject ReadDocument(string path)
    {
        if (!File.Exists(path))
        {
            throw new DaemonStartupException(
                $"缺少工作区配置：{path}\n"
                + "至少需要 models、channels、proactive、tools 四段（凭据写环境变量名，不写明文）。");
        }

        try
        {
            return JsonNode.Parse(File.ReadAllText(path)) as JsonObject
                   ?? throw new JsonException("根节点必须是对象");
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            throw new DaemonStartupException($"工作区配置无法读取：{path}（{ex.Message}）", ex);
        }
    }

    /// <summary>把 JSON 文档解析成配置对象；<paramref name="document"/> 的 <c>root</c> 会被覆盖成工作区根。</summary>
    /// <param name="document">整份配置文档（会被写入 root）。</param>
    /// <param name="workspaceRoot">工作区根目录。</param>
    public static WorkspaceConfig FromDocument(JsonObject document, string workspaceRoot)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        document["root"] = workspaceRoot;
        WorkspaceConfig? config;
        try
        {
            config = document.Deserialize<WorkspaceConfig>(JsonWire.Options);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            throw new DaemonStartupException($"工作区配置格式错误（{ex.Message}）", ex);
        }

        if (config is null || config.Models is null || config.Channels is null)
        {
            throw new DaemonStartupException("工作区配置不完整（models / channels 不能为空）");
        }

        // 下面两段没有「可空」语义：缺了会在运行期 NRE，启动时就拦下来。
        if (config.Proactive is null || config.Tools is null || config.Proactive.Heartbeat is null)
        {
            throw new DaemonStartupException("工作区配置不完整（proactive.heartbeat / tools 不能为空）");
        }

        return config;
    }
}
