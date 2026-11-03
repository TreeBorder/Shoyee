using System.Text.Json;
using Shoyee.Contracts.Domain;

namespace Shoyee.Contracts.Dtos;

/// <summary>审批请求（事件 <c>approval.request</c> 与 <c>approvals</c> 表共用）。</summary>
/// <param name="ApprovalId">审批 id。</param>
/// <param name="SessionId">所属会话；RP 用 Lane id 占位（RP 没有会话）。</param>
/// <param name="TurnId">所属轮次。</param>
/// <param name="ToolName">工具名。</param>
/// <param name="Command">命令裸原文（非命令类工具为 null，展示用）。</param>
/// <param name="Arguments">完整参数（<c>command</c> 字段同样是原文，不转义）。</param>
/// <param name="CreatedAt">发起时间（UTC）。</param>
/// <param name="ExpiresAt">超时时间（UTC），过期按拒绝处理。</param>
/// <param name="LaneId">所属 Lane（可见性过滤用）。</param>
public sealed record ApprovalRequestDto(
    string ApprovalId,
    string SessionId,
    string TurnId,
    string ToolName,
    string? Command,
    JsonElement? Arguments,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    string? LaneId = null);

/// <summary>审批结论事件。</summary>
/// <param name="ApprovalId">审批 id。</param>
/// <param name="Decision">结论。</param>
/// <param name="DecidedAt">时间（UTC）。</param>
/// <param name="SessionId">所属会话；RP 用 Lane id 占位。</param>
/// <param name="LaneId">所属 Lane。</param>
public sealed record ApprovalResolvedDto(
    string ApprovalId,
    ApprovalDecision Decision,
    DateTimeOffset DecidedAt,
    string? SessionId = null,
    string? LaneId = null);
