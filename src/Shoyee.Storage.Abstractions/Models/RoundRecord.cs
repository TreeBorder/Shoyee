using Microsoft.Extensions.AI;

namespace Shoyee.Storage.Abstractions;

/// <summary>RP 的一轮（还原结果）。</summary>
/// <param name="RoundNo">全局轮次号（RP 只有一条时间线，跨 Lane 单调）。</param>
/// <param name="LaneId">这一轮的来源 Lane。</param>
/// <param name="Request">本轮输入消息。</param>
/// <param name="Response">本轮响应消息（含工具调用/结果）。</param>
/// <param name="CreatedAt">时间（UTC）。</param>
public sealed record RoundRecord(
    int RoundNo,
    string LaneId,
    IReadOnlyList<ChatMessage> Request,
    IReadOnlyList<ChatMessage> Response,
    DateTimeOffset CreatedAt);
