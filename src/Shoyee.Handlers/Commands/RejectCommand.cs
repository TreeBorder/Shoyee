using Shoyee.Contracts.Abstractions;
using Shoyee.Contracts.Domain;

namespace Shoyee.Handlers.Commands;

/// <summary><c>/reject</c>：拒绝当前会话挂起的整批工具调用（她会收到拒绝并收手）。</summary>
public sealed class RejectCommand : ICommandHandler
{
    private readonly IApprovalConsole _approvals;

    /// <summary>创建指令。</summary>
    /// <param name="approvals">审批台。</param>
    public RejectCommand(IApprovalConsole approvals)
    {
        ArgumentNullException.ThrowIfNull(approvals);
        _approvals = approvals;
    }

    /// <inheritdoc />
    public string CommandName => "reject";

    /// <inheritdoc />
    public string Description => "拒绝操作";

    /// <inheritdoc />
    public async Task<string> HandleAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        if (_approvals.Peek(context.TargetId).Count == 0)
        {
            return "🙅 现在没有等你审批的操作。";
        }

        var resolved = await _approvals
            .ResolveAllAsync(context.TargetId, ApprovalDecision.Denied, "im:" + context.LaneId, "IM 指令拒绝", cancellationToken)
            .ConfigureAwait(false);
        return resolved == 0 ? "🙅 审批已经结束了（可能刚超时）。" : $"🛑 已拒绝 {resolved} 项。";
    }
}
