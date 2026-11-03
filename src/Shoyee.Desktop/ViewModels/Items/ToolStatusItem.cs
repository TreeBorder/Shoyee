namespace Shoyee.Desktop.ViewModels.Items;

/// <summary>设置页「工具」列表的一项（运行中来自 <c>tools.list</c>；未连接时是静态占位）。</summary>
public sealed class ToolStatusItem
{
    /// <summary>工具名。</summary>
    public required string Name { get; init; }

    /// <summary>所属组 / 来源（内置或 MCP 服务名）。</summary>
    public required string Group { get; init; }

    /// <summary>用途。</summary>
    public required string Description { get; init; }

    /// <summary>当前是否启用。</summary>
    public required bool IsEnabled { get; init; }

    /// <summary>是否需要审批。</summary>
    public bool RequiresApproval { get; init; }

    /// <summary>可见范围（all / work / roleplay）。</summary>
    public string Scope { get; init; } = "all";

    /// <summary>是否有副作用（写文件 / 调命令 / 发消息）。</summary>
    public bool Writes { get; init; }

    /// <summary>来源（builtin / mcp:服务名）。</summary>
    public string Source { get; init; } = "";

    /// <summary>范围的中文标签。</summary>
    public string ScopeLabel => Scope switch
    {
        "work" => "工作",
        "roleplay" => "角色",
        _ => "通用",
    };

    /// <summary>是否来自 MCP。</summary>
    public bool IsMcp => Source.StartsWith("mcp:", StringComparison.Ordinal);
}
