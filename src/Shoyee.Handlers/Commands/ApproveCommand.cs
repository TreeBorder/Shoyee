using Shoyee.Contracts.Abstractions;
using Shoyee.Contracts.Domain;

namespace Shoyee.Handlers.Commands;

/// <summary>
/// <c>/approve [always]</c>：批准当前会话挂起的整批工具调用。
/// 决定落下后，仍挂起的那一轮自己继续跑，最终正文照常回到原来的渠道。
/// </summary>
public sealed class ApproveCommand : ICommandHandler
{
    private readonly IApprovalConsole _approvals;

    /// <summary>创建指令。</summary>
    /// <param name="approvals">审批台。</param>
    public ApproveCommand(IApprovalConsole approvals)
    {
        ArgumentNullException.ThrowIfNull(approvals);
        _approvals = approvals;
    }

    /// <inheritdoc />
    public string CommandName => "approve";

    /// <inheritdoc />
    public string Description => "批准操作（/approve always 本会话不再问）";

    /// <inheritdoc />
    public async Task<string> HandleAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        if (_approvals.Peek(context.TargetId).Count == 0)
        {
            return "🙅 现在没有等你审批的操作。";
        }

        var decision = string.Equals(context.Arguments, "always", StringComparison.OrdinalIgnoreCase)
            ? ApprovalDecision.Always
            : ApprovalDecision.Approved;
        var resolved = await _approvals
            .ResolveAllAsync(context.TargetId, decision, "im:" + context.LaneId, "IM 指令批准", cancellationToken)
            .ConfigureAwait(false);
        return resolved == 0
            ? "🙅 审批已经结束了（可能刚超时）。"
            : decision == ApprovalDecision.Always
                ? "✅ 已批准（本会话内不再问），她继续。"
                : $"✅ 已批准 {resolved} 项，她继续。";
    }
}
