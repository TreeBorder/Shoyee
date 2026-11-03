using System.Text.RegularExpressions;
using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Domain;
using Shoyee.Desktop.ViewModels.Items;

namespace Shoyee.Desktop.Conversations;

/// <summary>一条投影后的 RP 消息：排序键是消息 id（同一消息拆出的多个气泡共享）。</summary>
/// <param name="SortKey">排序键（数据库消息 id，全局递增）。</param>
/// <param name="Item">气泡。</param>
public sealed record RoleplayRow(long SortKey, ChatMessageItem Item);

/// <summary>
/// 把 RPC 的 <see cref="MessageView"/> 投成 RP 气泡。
///
/// 和 <see cref="DaemonProjection"/> 的区别：RP 用「每个换行 / <c>&lt;br/&gt;</c> 拆段 + Markdown 图片独立成泡」，
/// 不把本地文件链接拆成文件气泡；每条都带渠道名（Lane 徽标）。
/// </summary>
public static partial class RoleplayProjection
{
    /// <summary>把一页 RP 消息投成气泡序列。</summary>
    /// <param name="messages">按 id 升序的消息。</param>
    /// <param name="laneName">渠道展示名（写进 <see cref="ChatMessageItem.LaneName"/>）。</param>
    public static IReadOnlyList<RoleplayRow> ToRows(
        IReadOnlyList<MessageView> messages,
        string? laneName)
    {
        var rows = new List<RoleplayRow>(messages.Count);
        var tools = new Dictionary<string, ChatMessageItem>(StringComparer.Ordinal);

        foreach (var message in messages)
        {
            var time = message.CreatedAt.ToLocalTime().ToString("HH:mm");
            var attachments = DaemonProjection.ToAttachments(message.Attachments);

            switch (message.Kind)
            {
                case MessageKind.ToolCall:
                {
                    // 工具调用前的话拆成正文气泡，过程行只保留调用参数 / 结果（刷新后与流式时一致）
                    if (!string.IsNullOrWhiteSpace(message.Text))
                    {
                        rows.AddRange(ToBubbleRows(message.Id, message.Text, time, laneName, attachments));
                    }

                    var item = new ChatMessageItem
                    {
                        Kind = ChatEntryKind.Tool,
                        Text = "",
                        ToolName = message.ToolName,
                        ToolCallId = message.ToolCallId,
                        Detail = message.ToolArguments ?? "",
                        Time = time,
                        LaneName = laneName,
                    };
                    rows.Add(new RoleplayRow(message.Id, item));
                    if (!string.IsNullOrWhiteSpace(message.ToolCallId))
                    {
                        tools[message.ToolCallId] = item;
                    }

                    break;
                }

                case MessageKind.ToolResult:
                {
                    if (message.ToolCallId is { Length: > 0 } callId && tools.TryGetValue(callId, out var call))
                    {
                        call.Result = message.ToolResult ?? message.Text;
                        if (attachments is { Count: > 0 })
                        {
                            call.Attachments = [.. call.Attachments, .. attachments];
                        }

                        break;
                    }

                    rows.Add(new RoleplayRow(
                        message.Id,
                        new ChatMessageItem
                        {
                            Kind = ChatEntryKind.Tool,
                            Text = message.Text,
                            ToolName = message.ToolName,
                            ToolCallId = message.ToolCallId,
                            Result = message.ToolResult ?? "",
                            Time = time,
                            LaneName = laneName,
                            Attachments = attachments ?? [],
                        }));
                    break;
                }

                case MessageKind.SystemInput:
                {
                    rows.Add(new RoleplayRow(
                        message.Id,
                        new ChatMessageItem
                        {
                            Kind = ChatEntryKind.SystemInput,
                            Text = message.Text,
                            Time = time,
                            LaneName = laneName,
                            Attachments = attachments,
                        }));
                    break;
                }

                case MessageKind.Summary:
                {
                    rows.Add(new RoleplayRow(
                        message.Id,
                        new ChatMessageItem
                        {
                            Kind = ChatEntryKind.System,
                            Text = message.Text,
                            Time = time,
                            LaneName = laneName,
                        }));
                    break;
                }

                default:
                {
                    if (message.Role == MessageRole.System)
                    {
                        break;
                    }

                    if (!string.IsNullOrWhiteSpace(message.Thinking))
                    {
                        rows.Add(new RoleplayRow(
                            message.Id,
                            new ChatMessageItem
                            {
                                Kind = ChatEntryKind.Reasoning,
                                Text = ConversationText.ClampReasoning(message.Thinking),
                                Time = time,
                                LaneName = laneName,
                            }));
                    }

                    if (message.Role == MessageRole.User)
                    {
                        // 历史里的系统注入（心跳 / cron / 状态提示）折叠成过程行，默认隐藏。
                        if (ConversationText.IsSystemInjection(message.Text))
                        {
                            rows.Add(new RoleplayRow(
                                message.Id,
                                new ChatMessageItem
                                {
                                    Kind = ChatEntryKind.SystemInput,
                                    Text = message.Text,
                                    Time = time,
                                    LaneName = laneName,
                                    Attachments = attachments,
                                }));
                            break;
                        }

                        var text = ConversationText.ToPlainText(CleanUserText(message.Text));
                        if (text.Length == 0)
                        {
                            break;
                        }

                        rows.Add(new RoleplayRow(
                            message.Id,
                            new ChatMessageItem
                            {
                                Kind = ChatEntryKind.User,
                                Text = text,
                                Time = time,
                                LaneName = laneName,
                                Attachments = attachments,
                            }));
                        break;
                    }

                    if (message.Role == MessageRole.Assistant && message.Kind == MessageKind.Text)
                    {
                        rows.AddRange(ToBubbleRows(message.Id, message.Text, time, laneName, attachments));
                        break;
                    }

                    rows.Add(new RoleplayRow(
                        message.Id,
                        new ChatMessageItem
                        {
                            Kind = message.Role == MessageRole.Assistant
                                ? ChatEntryKind.Assistant
                                : ChatEntryKind.System,
                            Text = message.Text,
                            Time = time,
                            Attachments = attachments,
                            LaneName = laneName,
                        }));
                    break;
                }
            }
        }

        return rows;
    }

    /// <summary>
    /// 助手正文 → RP 气泡：段落各成一段、Markdown 图片独立成图片泡。
    /// 附件挂在最后一个正文气泡上；正文只有图片/附件时补一个空正文气泡承载附件。
    /// </summary>
    /// <param name="sortKey">消息 id。</param>
    /// <param name="text">助手正文。</param>
    /// <param name="time">时间文本。</param>
    /// <param name="laneName">渠道展示名。</param>
    /// <param name="attachments">附件卡片。</param>
    public static IReadOnlyList<RoleplayRow> ToBubbleRows(
        long sortKey,
        string text,
        string time,
        string? laneName,
        IReadOnlyList<AttachmentItem>? attachments = null)
    {
        var bubbles = RoleplayBubbleParser.Split(text);
        if (bubbles.Count == 0)
        {
            if (attachments is not { Count: > 0 })
            {
                return [];
            }

            return
            [
                new RoleplayRow(sortKey, new ChatMessageItem
                {
                    Kind = ChatEntryKind.Assistant,
                    Time = time,
                    LaneName = laneName,
                    Attachments = attachments,
                }),
            ];
        }

        var rows = new List<RoleplayRow>(bubbles.Count);
        foreach (var bubble in bubbles)
        {
            rows.Add(new RoleplayRow(sortKey, new ChatMessageItem
            {
                Kind = bubble.IsImage ? ChatEntryKind.Image : ChatEntryKind.Assistant,
                Text = bubble.Text,
                ImageSource = bubble.ImageSource,
                Time = time,
                LaneName = laneName,
            }));
        }

        if (attachments is { Count: > 0 })
        {
            var target = rows.LastOrDefault(static row => row.Item.IsAssistant)
                         ?? rows[^1];
            target.Item.Attachments = attachments;
        }

        return rows;
    }

    /// <summary>系统注入（心跳 / cron / 状态提示）不是用户说的话，不进气泡。</summary>
    /// <summary>去掉历史入站消息里的 <c>&lt;Time&gt;…&lt;/Time&gt;</c> 与 <c>&lt;UserMessage&gt;</c> 包裹。</summary>
    private static string CleanUserText(string text)
    {
        var cleaned = TimeBlock().Replace(text, "");
        cleaned = UserMessageTag().Replace(cleaned, "");
        return cleaned.Trim();
    }

    [GeneratedRegex(@"<Time>.*?</Time>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex TimeBlock();

    [GeneratedRegex(@"</?UserMessage>", RegexOptions.IgnoreCase)]
    private static partial Regex UserMessageTag();
}
