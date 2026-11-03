using Microsoft.Extensions.AI;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;

namespace Shoyee.Sessions.Turns;

/// <summary>
/// 出站附件挂到「最后一条可见助手正文」上。
///
/// 附件属于整轮回复，不属于某次工具调用；挂到正文上，桌面/IM 才能把它和那句话放在一起。
/// 本轮没有正文（只调了工具、或模型没说话）时不挂——宁可这次不带附件，
/// 也不要把它贴到上一轮的旧消息上。
/// </summary>
public static class TurnAttachments
{
    /// <summary>
    /// 在 <paramref name="messages"/> 的 <paramref name="startIndex"/> 之后找最后一条可见助手正文并打附件标记。
    /// </summary>
    /// <param name="messages">本轮新增的消息（RP 是本轮响应；工作模式是整个会话历史的一段）。</param>
    /// <param name="attachments">要挂的附件；空 = 什么都不做。</param>
    /// <param name="startIndex">只在这个下标之后找（工作模式用来排除历史轮次）。</param>
    /// <returns>被标记的消息；没找到可挂的正文时返回 null。</returns>
    public static ChatMessage? AttachToLastAssistantText(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<MessageAttachment>? attachments,
        int startIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(messages);
        if (attachments is not { Count: > 0 })
        {
            return null;
        }

        ChatMessage? target = null;
        for (var i = Math.Max(0, startIndex); i < messages.Count; i++)
        {
            var message = messages[i];
            if (message.Role != ChatRole.Assistant
                || IsSystemInput(message)
                || !message.Contents.OfType<TextContent>().Any(static text => !string.IsNullOrWhiteSpace(text.Text)))
            {
                continue;
            }

            target = message;
        }

        if (target is null)
        {
            return null;
        }

        target.AdditionalProperties ??= new AdditionalPropertiesDictionary();
        target.AdditionalProperties[ChatMessageMarkers.Attachments] = attachments;
        return target;
    }

    private static bool IsSystemInput(ChatMessage message)
        => message.AdditionalProperties is { } props
           && props.TryGetValue(ChatMessageMarkers.SystemInput, out var value)
           && value is true;
}
