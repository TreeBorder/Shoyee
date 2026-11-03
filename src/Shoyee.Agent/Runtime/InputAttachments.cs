using System.Text;
using Microsoft.Extensions.AI;
using Shoyee.Agent.Media;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;

namespace Shoyee.Agent.Runtime;

/// <summary>
/// 本轮附件进上下文的方式：给模型的正文里补上本地路径（她能直接读），
/// 有自动分析结果时再补一段文字（图片描述 / 语音转写），
/// 同时用标记把「给人看的正文 + 结构化附件」带给投影层——两者不混在一起。
/// </summary>
internal static class InputAttachments
{
    /// <summary>模型看到的正文（原文 + 附件路径清单 + 自动分析结果）。</summary>
    public static string ComposeModelText(
        string text,
        IReadOnlyList<MessageAttachment>? attachments,
        IReadOnlyList<AttachmentAnalysis>? analyses = null)
    {
        if (attachments is not { Count: > 0 })
        {
            return text;
        }

        var builder = new StringBuilder(text);
        foreach (var attachment in attachments)
        {
            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            builder.Append("[附件] ").Append(attachment.Path)
                .Append(" (").Append(attachment.Mime).Append(')');
        }

        if (analyses is { Count: > 0 })
        {
            foreach (var analysis in analyses)
            {
                builder.Append('\n').Append(analysis.Text);
            }
        }

        return builder.ToString();
    }

    /// <summary>给落库的输入消息打标记：可见正文与结构化附件。</summary>
    public static ChatMessage Tag(
        ChatMessage message,
        string displayText,
        IReadOnlyList<MessageAttachment>? attachments)
    {
        message.AdditionalProperties ??= new AdditionalPropertiesDictionary();

        // 可见正文总是单列：忙时合并会把系统注入（进程回执等）拼进模型正文，
        // 但那部分不该出现在桌面/IM 的可见历史里。
        message.AdditionalProperties[ChatMessageMarkers.DisplayText] = displayText;

        if (attachments is { Count: > 0 })
        {
            message.AdditionalProperties[ChatMessageMarkers.Attachments] = attachments;
        }

        return message;
    }
}
