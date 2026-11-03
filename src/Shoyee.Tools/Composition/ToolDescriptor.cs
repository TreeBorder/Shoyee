using Microsoft.Extensions.AI;

namespace Shoyee.Tools.Composition;

/// <summary>一个工具的元数据（不靠命名前缀区分专用工具）。</summary>
/// <param name="Name">工具名（MCP 防冒名与审批规则都按它匹配）。</param>
/// <param name="Scope">可见范围。</param>
/// <param name="Writes">是否有副作用（写文件/执行/改库）。</param>
/// <param name="RequiresApproval">是否挂审批（只有 Work profile 有审批中间件）。</param>
public sealed record ToolMetadata(string Name, ToolScope Scope, bool Writes, bool RequiresApproval);

/// <summary>工具 + 元数据；由 <see cref="ToolsetBuilder"/> 过滤、包装。</summary>
/// <param name="Tool">实际交给模型的工具。</param>
/// <param name="Metadata">工具元数据。</param>
public sealed record ToolDescriptor(AITool Tool, ToolMetadata Metadata)
{
    /// <summary>用一个工具和它的元数据构造描述。</summary>
    public static ToolDescriptor Create(AITool tool, ToolScope scope, bool writes, bool requiresApproval)
    {
        ArgumentNullException.ThrowIfNull(tool);
        var name = tool is AIFunction function && !string.IsNullOrWhiteSpace(function.Name)
            ? function.Name
            : tool.GetType().Name;
        return new ToolDescriptor(tool, new ToolMetadata(name, scope, writes, requiresApproval));
    }
}
