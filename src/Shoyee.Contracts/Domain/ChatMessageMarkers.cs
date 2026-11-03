namespace Shoyee.Contracts.Domain;

/// <summary>
/// <see cref="Microsoft.Extensions.AI.ChatMessage.AdditionalProperties"/> 里的约定标记。
///
/// 这些键跟着 <c>ChatMessage</c> 走（会话序列化、轮次落库都用同一份），
/// 存储层投影时据此决定「这条消息算不算可见正文」。
/// </summary>
public static class ChatMessageMarkers
{
    /// <summary>摘要消息（框架压缩分组产物）。</summary>
    public const string Summary = "_is_summary";

    /// <summary>她主动发出的消息（心跳/新话题）。</summary>
    public const string Spoken = "_spoken";

    /// <summary>
    /// 系统注入（心跳/命令回执等）：进入模型上下文，但**不是她或用户的可见消息**，
    /// 不落消息投影、不进检索、不出现在桌面历史里。
    /// </summary>
    public const string SystemInput = "_system_input";

    /// <summary>给模型看的正文（带时间/渠道/附件路径）与给人看的正文不同时，后者放这里。</summary>
    public const string DisplayText = "_display_text";

    /// <summary>本轮附件（<see cref="Dtos.MessageAttachment"/> 列表）；投影时落到 <c>messages.attachments_json</c>。</summary>
    public const string Attachments = "_attachments";
}
