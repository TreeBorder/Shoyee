using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Shoyee.Tools.Composition;

/// <summary>组装好的工具面：工具列表 + 审批自动放行规则（挂到 <c>ToolApprovalAgentOptions.AutoApprovalRules</c>）。</summary>
/// <param name="Tools">交给 <c>ChatOptions.Tools</c> 的工具。</param>
/// <param name="AutoApprovalRules">审批规则链（顺序敏感）；RP 为空。</param>
public sealed record Toolset(
    IReadOnlyList<AITool> Tools,
    IReadOnlyList<Func<ToolAutoApprovalRuleContext, ValueTask<bool>>> AutoApprovalRules)
{
    /// <summary>空工具面（测试/不启用任何工具时）。</summary>
    public static Toolset Empty { get; } = new([], []);
}
