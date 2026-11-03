using Microsoft.Extensions.AI;

namespace Shoyee.Agent.Runtime;

/// <summary>
/// 审批回调：把框架浮出的一批待审批调用交给调用方（通常是 <see cref="Approval.ApprovalBroker"/>），
/// 拿回与之配对的 <see cref="ToolApprovalResponseContent"/> 继续跑。
/// </summary>
/// <param name="requests">本批待审批调用。</param>
/// <param name="cancellationToken">取消令牌（轮次被取消时触发）。</param>
public delegate Task<IReadOnlyList<AIContent>> ApprovalHandler(
    IReadOnlyList<ToolApprovalRequestContent> requests,
    CancellationToken cancellationToken = default);
