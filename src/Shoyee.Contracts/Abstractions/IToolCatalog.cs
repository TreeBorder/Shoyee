using Shoyee.Contracts.Dtos;

namespace Shoyee.Contracts.Abstractions;

/// <summary>当前工具面清单（<c>tools.list</c>）：设置页展示挂载了什么、哪些被禁用。</summary>
public interface IToolCatalog
{
    /// <summary>全部工具（两类 profile 的并集，含禁用的）。</summary>
    IReadOnlyList<ToolInfo> List();
}

/// <summary>MCP 连接管理（<c>mcp.status</c> / <c>mcp.reload</c>）。</summary>
public interface IMcpControl
{
    /// <summary>各服务当前状态（含未启用与连接失败的）。</summary>
    IReadOnlyList<McpServerStatus> Status { get; }

    /// <summary>按当前生效的工作区配置重连全部服务，返回新状态。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<McpReloadResult> ReloadAsync(CancellationToken cancellationToken = default);
}
