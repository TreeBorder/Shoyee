namespace Shoyee.Storage.Entities.Chat;

/// <summary>
/// RP 的一轮：一行一轮，结构照 Harem 的 <c>ChatRawMessage</c>。
/// 轮次按 Lane 计（RP 没有 session 概念），<c>round_no</c> 单调递增、裁剪不清零。
/// </summary>
public sealed class RoundEntity
{
    /// <summary>自增 id。</summary>
    public long Id { get; set; }

    /// <summary>所属 Lane（RP 的连续流）。</summary>
    public string LaneId { get; set; } = string.Empty;

    /// <summary>第几轮。</summary>
    public int RoundNo { get; set; }

    /// <summary>本轮输入消息（<c>ChatMessage[]</c> JSON）。</summary>
    public string RequestJson { get; set; } = string.Empty;

    /// <summary>本轮响应消息（<c>ChatMessage[]</c> JSON，含工具调用/结果）。</summary>
    public string ResponseJson { get; set; } = string.Empty;

    /// <summary>时间（UTC）。</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
