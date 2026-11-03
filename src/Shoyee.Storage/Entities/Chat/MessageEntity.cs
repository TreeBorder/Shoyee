using Shoyee.Contracts.Domain;

namespace Shoyee.Storage.Entities.Chat;

/// <summary>
/// 消息投影：检索、桌面列表、导出用，**不是真源**。
/// RP 由轮次派生，工作模式由会话转录派生；正文进 FTS。
/// </summary>
public sealed class MessageEntity
{
    /// <summary>自增 id（游标）。</summary>
    public long Id { get; set; }

    /// <summary>所属 Lane（两种模式都有）。</summary>
    public string LaneId { get; set; } = string.Empty;

    /// <summary>所属工作会话；RP 为 null。</summary>
    public string? SessionId { get; set; }

    /// <summary>所属轮次；工作模式为 null。</summary>
    public int? RoundNo { get; set; }

    /// <summary>
    /// 该消息在会话历史里的下标（工作模式专用；RP 为 null）。
    /// 增量补抄靠它定位「抄到哪了」，不是对外 id。
    /// </summary>
    public int? SourceIndex { get; set; }

    /// <summary>框架 <c>ChatRole.Value</c> 原值。</summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>派生种类（见架构文档 §3.3）。</summary>
    public MessageKind Kind { get; set; }

    /// <summary>仅可见正文（工具结果为空）。</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>工具名（工具调用/结果行）。</summary>
    public string? ToolName { get; set; }

    /// <summary>思考（reasoning）文本（框架历史里带了才写）。</summary>
    public string? Thinking { get; set; }

    /// <summary>工具调用 id（调用行与结果行配对）。</summary>
    public string? ToolCallId { get; set; }

    /// <summary>工具参数（JSON 文本；调用行）。</summary>
    public string? ToolArguments { get; set; }

    /// <summary>工具返回（文本化并截断；结果行）。</summary>
    public string? ToolResult { get; set; }

    /// <summary>附件（JSON 数组，本地文件引用；null = 没有附件）。</summary>
    public string? AttachmentsJson { get; set; }

    /// <summary>时间（UTC）。</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
