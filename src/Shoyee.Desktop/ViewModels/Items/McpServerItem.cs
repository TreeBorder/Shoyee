namespace Shoyee.Desktop.ViewModels.Items;

/// <summary>设置页「MCP 连接」的一项（运行中来自 <c>mcp.status</c>；未连接时是静态占位）。</summary>
public sealed class McpServerItem
{
    /// <summary>服务名。</summary>
    public required string Name { get; init; }

    /// <summary>传输方式或地址。</summary>
    public required string Endpoint { get; init; }

    /// <summary>连接状态（已连接 / 未连接 / 已停用 / 连接失败）。</summary>
    public required string Status { get; init; }

    /// <summary>状态是否是正常状态。</summary>
    public required bool IsHealthy { get; init; }

    /// <summary>配置里是否启用。</summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>拿到的工具数。</summary>
    public int ToolCount { get; init; }

    /// <summary>工具名清单。</summary>
    public IReadOnlyList<string> Tools { get; init; } = [];

    /// <summary>工具清单文案。</summary>
    public string ToolsText => ToolCount switch
    {
        0 => "没有工具",
        1 => "1 个工具",
        _ => $"{ToolCount} 个工具",
    };

    /// <summary>连接失败原因；正常为空。</summary>
    public string Error { get; init; } = "";

    /// <summary>是否有错误。</summary>
    public bool HasError => Error.Length > 0;
}
