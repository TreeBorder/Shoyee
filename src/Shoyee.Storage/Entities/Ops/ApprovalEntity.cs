using Shoyee.Contracts.Domain;

namespace Shoyee.Storage.Entities.Ops;

/// <summary>审批记录（待审批 + 结论）。</summary>
public sealed class ApprovalEntity
{
    /// <summary>自增 id。</summary>
    public long Id { get; set; }

    /// <summary>审批 id（对客户端可见）。</summary>
    public string RequestId { get; set; } = string.Empty;

    /// <summary>所属会话。</summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>所属轮次。</summary>
    public string TurnId { get; set; } = string.Empty;

    /// <summary>工具名。</summary>
    public string ToolName { get; set; } = string.Empty;

    /// <summary>参数（JSON；command 保留原文）。</summary>
    public string Arguments { get; set; } = string.Empty;

    /// <summary>结论；null = 待定。</summary>
    public ApprovalDecision? Decision { get; set; }

    /// <summary>决定者（desktop / im / timeout / rule）。</summary>
    public string? DecidedBy { get; set; }

    /// <summary>决定时间（UTC）。</summary>
    public DateTimeOffset? DecidedAt { get; set; }

    /// <summary>创建时间（UTC）。</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
