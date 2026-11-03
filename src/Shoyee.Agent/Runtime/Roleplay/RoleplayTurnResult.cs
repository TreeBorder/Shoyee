using Microsoft.Extensions.AI;
using Shoyee.Contracts.Dtos;
using Shoyee.Storage.Abstractions;

namespace Shoyee.Agent.Runtime.Roleplay;

/// <summary>一轮 RP 的结果：落库的轮次（没有响应时为 null）。</summary>
/// <param name="Round">已写入 <c>rounds</c> 的轮次。</param>
/// <param name="Response">本轮完整响应消息（含工具调用/结果）。</param>
/// <param name="Usage">本轮模型用量（端点不报时为 0）。</param>
/// <param name="MessageId">本轮落库的助手消息 id（投影真值；没有可见正文时为 null）。</param>
/// <param name="Attachments">本轮她说出口的附件（已随助手消息落库）。</param>
/// <param name="Calls">本轮每次模型调用的用量明细（工具循环会多次；端点不报时为空）。</param>
public sealed record RoleplayTurnResult(
    RoundRecord Round,
    IReadOnlyList<ChatMessage> Response,
    UsageSummary? Usage = null,
    long? MessageId = null,
    IReadOnlyList<MessageAttachment>? Attachments = null,
    IReadOnlyList<UsageCall>? Calls = null);
