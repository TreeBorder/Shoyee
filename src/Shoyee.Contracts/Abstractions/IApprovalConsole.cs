using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;

namespace Shoyee.Contracts.Abstractions;

/// <summary>
/// 审批台的「按会话批量看/批量答复」面：IM 侧的 <c>/approve</c> <c>/reject</c> 没有审批 id，
/// 只能按会话把当前挂起的整批一起处理。实现在 Agent 的 <c>ApprovalBroker</c>。
/// </summary>
public interface IApprovalConsole
{
    /// <summary>该会话当前的待审批请求（无则空）。</summary>
    /// <param name="sessionOrLaneId">会话 id；RP 用 Lane id。</param>
    IReadOnlyList<ApprovalRequestDto> Peek(string sessionOrLaneId);

    /// <summary>把该会话当前挂起的整批审批一次落决定（一屏审批共用一个结论）。</summary>
    /// <param name="sessionOrLaneId">会话 id；RP 用 Lane id。</param>
    /// <param name="decision">结论（不能是 <see cref="ApprovalDecision.Expired"/>）。</param>
    /// <param name="decidedBy">决定者（桌面 / IM / 超时）。</param>
    /// <param name="note">备注。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>实际落决定的批次数；0 = 没有待审批。</returns>
    Task<int> ResolveAllAsync(
        string sessionOrLaneId,
        ApprovalDecision decision,
        string decidedBy,
        string? note = null,
        CancellationToken cancellationToken = default);
}
