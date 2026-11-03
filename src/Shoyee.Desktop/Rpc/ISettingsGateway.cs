using System.Text.Json;
using Shoyee.Contracts.Dtos;

namespace Shoyee.Desktop.Rpc;

/// <summary>
/// 设置页「只读接数」需要的 RPC 面：运行配置、工作区路径、工具面与 MCP 状态。
/// 写入口（config.set / state.set / mcp.reload）下一步再接。
/// </summary>
public interface ISettingsGateway
{
    /// <summary>工作连接是否可用。</summary>
    bool IsConnected { get; }

    /// <summary>读取 daemon 当前生效的配置（凭据只有环境变量名）。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<JsonElement> GetConfigAsync(CancellationToken cancellationToken = default);

    /// <summary>工作区里各固定文件 / 目录的绝对路径。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<WorkspacePathsDto> GetWorkspacePathsAsync(CancellationToken cancellationToken = default);

    /// <summary>当前工具面。</summary>
    /// <param name="scope">只看 work / roleplay；null = 全部。</param>
    /// <param name="query">按名字 / 说明过滤。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<IReadOnlyList<ToolInfo>> ListToolsAsync(
        string? scope = null,
        string? query = null,
        CancellationToken cancellationToken = default);

    /// <summary>MCP 服务的连接状态与工具清单。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<IReadOnlyList<McpServerStatus>> GetMcpStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>运行状态：Lane、全局状态（心情/思念等）与用量。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<StatusDto> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>改一个通用状态（角色状态页）。</summary>
    /// <param name="key">状态键。</param>
    /// <param name="value">新值。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<StateDto> SetStateAsync(string key, string value, CancellationToken cancellationToken = default);

    /// <summary>热重载配置（只影响新会话）。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<ConfigReloadResult> ReloadConfigAsync(CancellationToken cancellationToken = default);

    /// <summary>局部写配置（设置页每页只发自己那一段），写盘后按热更新规则生效。</summary>
    /// <param name="patch">局部配置（深合并）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<ConfigSetResult> SetConfigAsync(
        System.Text.Json.Nodes.JsonObject patch,
        CancellationToken cancellationToken = default);

    /// <summary>重连全部 MCP 服务；工具名集合变了要重启 daemon 才生效。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<McpReloadResult> ReloadMcpAsync(CancellationToken cancellationToken = default);

    /// <summary>资料索引列表。</summary>
    /// <param name="scope">all / work / roleplay；null = 不限。</param>
    /// <param name="query">关键词过滤。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<IReadOnlyList<IndexEntry>> ListIndexAsync(
        string? scope = null,
        string? query = null,
        CancellationToken cancellationToken = default);

    /// <summary>新增或按 key 覆盖索引条目。</summary>
    /// <param name="entry">条目。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<IndexEntry> UpsertIndexAsync(IndexEntry entry, CancellationToken cancellationToken = default);

    /// <summary>按 key 删除索引条目。</summary>
    /// <param name="key">稳定键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task RemoveIndexAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>重启 daemon（退出后由 launchd 拉起；无守护进程时等于停机）。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    Task RestartDaemonAsync(CancellationToken cancellationToken = default);
}
