using Shoyee.Contracts.Domain;

namespace Shoyee.Storage.Abstractions;

/// <summary>一条审批记录。</summary>
/// <param name="RequestId">审批 id（对客户端可见）。</param>
/// <param name="SessionId">所属会话。</param>
/// <param name="TurnId">所属轮次。</param>
/// <param name="ToolName">工具名。</param>
/// <param name="ArgumentsJson">参数 JSON（command 保留原文）。</param>
/// <param name="Decision">结论；null = 待定。</param>
/// <param name="DecidedBy">决定者。</param>
/// <param name="DecidedAt">决定时间。</param>
/// <param name="CreatedAt">创建时间。</param>
public sealed record ApprovalRecord(
    string RequestId,
    string SessionId,
    string TurnId,
    string ToolName,
    string ArgumentsJson,
    ApprovalDecision? Decision,
    string? DecidedBy,
    DateTimeOffset? DecidedAt,
    DateTimeOffset CreatedAt);
