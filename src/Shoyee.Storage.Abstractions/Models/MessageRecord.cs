using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;

namespace Shoyee.Storage.Abstractions;

/// <summary>一条消息投影（不暴露存储实体）。</summary>
/// <param name="Id">自增 id（游标）。</param>
/// <param name="LaneId">所属 Lane。</param>
/// <param name="SessionId">所属工作会话；RP 为 null。</param>
/// <param name="RoundNo">所属轮次；工作模式为 null。</param>
/// <param name="Role">框架角色原值。</param>
/// <param name="Kind">派生种类。</param>
/// <param name="Content">可见正文。</param>
/// <param name="ToolName">工具名。</param>
/// <param name="CreatedAt">时间（UTC）。</param>
/// <param name="Attachments">附件（本地文件引用）；没有时为 null。</param>
/// <param name="Thinking">思考（reasoning）文本；框架历史里没有时为 null。</param>
/// <param name="ToolCallId">工具调用 id。</param>
/// <param name="ToolArguments">工具参数（JSON 文本）。</param>
/// <param name="ToolResult">工具返回（文本化并截断）。</param>
public sealed record MessageRecord(
    long Id,
    string LaneId,
    string? SessionId,
    int? RoundNo,
    string Role,
    MessageKind Kind,
    string Content,
    string? ToolName,
    DateTimeOffset CreatedAt,
    IReadOnlyList<MessageAttachment>? Attachments = null,
    string? Thinking = null,
    string? ToolCallId = null,
    string? ToolArguments = null,
    string? ToolResult = null);
