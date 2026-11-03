using Shoyee.Contracts.Domain;

namespace Shoyee.Storage.Abstractions;

/// <summary>
/// ⚠️ 预置仓储：暂无调用方，接口可能随对应模块（Agent 审批 / Channels / Proactive）调整。
/// </summary>
/// <summary>审批记录仓库。</summary>
public interface IApprovalRepository
{
    /// <summary>登记一个待审批请求。</summary>
    Task AddAsync(ApprovalRecord record, CancellationToken cancellationToken = default);

    /// <summary>按审批 id 取。</summary>
    Task<ApprovalRecord?> GetAsync(string requestId, CancellationToken cancellationToken = default);

    /// <summary>该会话当前待审批（未决定）的请求。</summary>
    Task<IReadOnlyList<ApprovalRecord>> GetPendingAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>写入结论。</summary>
    Task<bool> ResolveAsync(
        string requestId,
        ApprovalDecision decision,
        string decidedBy,
        DateTimeOffset? at = null,
        CancellationToken cancellationToken = default);

    /// <summary>把超过 <paramref name="before"/> 仍未决定的请求标为过期（返回条数）。</summary>
    Task<int> ExpireBeforeAsync(DateTimeOffset before, CancellationToken cancellationToken = default);
}
