namespace Shoyee.Contracts.Dtos;

/// <summary>工具面里的一个工具（设置页「工具集」用）。</summary>
/// <param name="Name">工具名（MCP 已加服务前缀）。</param>
/// <param name="Description">给模型看的说明。</param>
/// <param name="Scope">可见范围：all / work / roleplay。</param>
/// <param name="Enabled">是否启用（<c>tools.disabled</c> 里没有它）。</param>
/// <param name="RequiresApproval">工作模式是否要审（MCP 未包装，恒 false）。</param>
/// <param name="Writes">是否有副作用。</param>
/// <param name="Source">来源：<c>builtin</c> 或 <c>mcp:服务名</c>。</param>
public sealed record ToolInfo(
    string Name,
    string Description,
    string Scope,
    bool Enabled,
    bool RequiresApproval,
    bool Writes,
    string Source);

/// <summary><c>tools.list</c> 的请求。</summary>
/// <param name="Scope">只看某一档（work / roleplay）；null = 全部。</param>
/// <param name="Query">按名字/说明过滤；null = 不过滤。</param>
public sealed record ToolListRequest(string? Scope = null, string? Query = null);

/// <summary><c>tools.list</c> 的返回。</summary>
/// <param name="Items">工具清单。</param>
public sealed record ToolListResult(IReadOnlyList<ToolInfo> Items);

/// <summary>一个 MCP 服务的连接状态与工具清单。</summary>
/// <param name="Name">配置里的服务名（工具名前缀）。</param>
/// <param name="Url">端点。</param>
/// <param name="Enabled">配置里是否启用。</param>
/// <param name="Connected">本次进程是否连上。</param>
/// <param name="Tools">实际拿到的工具名（含服务前缀）。</param>
/// <param name="Error">连接失败原因；正常为 null。</param>
public sealed record McpServerStatus(
    string Name,
    string Url,
    bool Enabled,
    bool Connected,
    IReadOnlyList<string> Tools,
    string? Error);

/// <summary><c>mcp.status</c> 的返回。</summary>
/// <param name="Servers">全部配置过的服务（含未启用/连接失败的）。</param>
public sealed record McpStatusResult(IReadOnlyList<McpServerStatus> Servers);

/// <summary>
/// <c>mcp.reload</c> 的返回。
/// <paramref name="RestartRequired"/> = 工具名集合变了：新工具面已经收好，但运行中的 agent
/// 在启动时就把工具定义烘进了请求前缀，要重启 daemon 才生效（否则等于中途换工具，破坏 prompt cache）。
/// </summary>
/// <param name="Servers">重连后的服务状态。</param>
/// <param name="RestartRequired">是否要重启 daemon 才能让新工具面生效。</param>
public sealed record McpReloadResult(IReadOnlyList<McpServerStatus> Servers, bool RestartRequired);

/// <summary><c>state.set</c> 的请求。</summary>
/// <param name="Key">状态键（如 mood / energy）。</param>
/// <param name="Value">新值（统一字符串；数值状态自己保持数字格式）。</param>
public sealed record StateSetRequest(string Key, string Value);

/// <summary><c>usage.get</c> 的请求（会话与时间区间可同时给）。</summary>
/// <param name="SessionId">只看某个会话；null = 全部。</param>
/// <param name="From">起始时间（UTC）；null = 今天 00:00 UTC。</param>
/// <param name="To">结束时间（UTC）；null = 现在。</param>
public sealed record UsageGetRequest(
    string? SessionId = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null);

/// <summary><c>usage.get</c> 的返回。</summary>
/// <param name="Summary">汇总。</param>
/// <param name="From">实际使用的起始时间。</param>
/// <param name="To">实际使用的结束时间。</param>
public sealed record UsageGetResult(UsageSummary Summary, DateTimeOffset From, DateTimeOffset To);
