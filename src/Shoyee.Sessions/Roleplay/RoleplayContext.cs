using Microsoft.Extensions.AI;

namespace Shoyee.Sessions.Roleplay;

/// <summary>一轮 RP 组装结果。</summary>
/// <param name="Messages">发给模型的完整消息列表（背景区 → 摘要区 → 注入区 → 历史区 → 本轮输入）。</param>
/// <param name="Input">
/// 本轮发给模型的输入消息（已带时间与当前渠道标签），也就是 <paramref name="Messages"/> 的最后一条。
/// </param>
/// <param name="StoredInput">
/// 本轮落库用的输入消息（只有时间，不带渠道标签）。渠道标记在组装时按 Lane 切换重新注入，
/// 不写进原文，避免下一轮重复标记、也避免每条消息都背一份渠道文本。
/// </param>
public sealed record RoleplayContext(
    IReadOnlyList<ChatMessage> Messages,
    ChatMessage Input,
    ChatMessage StoredInput);
