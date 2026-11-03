using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;
using Shoyee.Desktop.ViewModels.Items;

namespace Shoyee.Desktop.Conversations;

/// <summary>
/// 把 RPC 的 <see cref="SessionSummary"/> / <see cref="MessageView"/> 投成桌面列表项与气泡。
/// 只做展示层映射；内容清洗（注入标签剥离等）在服务端已经做完。
/// </summary>
public static class DaemonProjection
{
    /// <summary>会话列表项。</summary>
    /// <param name="summary">RPC 返回的会话摘要。</param>
    public static SessionItem ToSessionItem(SessionSummary summary)
    {
        var origin = ChannelName(summary.Origin);
        return new SessionItem
        {
            Id = summary.Id,
            Title = string.IsNullOrWhiteSpace(summary.Title) ? "未命名会话" : summary.Title,
            Preview = summary.Archived
                ? $"已归档 · {summary.MessageCount} 条消息"
                : $"{summary.MessageCount} 条消息 · 点开载入历史",
            Time = ConversationText.FormatSessionTime(summary.LastActiveAt),
            Source = origin,
            IsExternal = summary.Origin != LaneChannel.Desktop,
            IsArchived = summary.Archived,
            MessageCount = summary.MessageCount,
            // 列表按最后活跃排序（服务端 session.list 的顺序），创建时间单独展示
            CreatedAt = summary.LastActiveAt,
            CreatedText = summary.CreatedAt.ToLocalTime().ToString("MM-dd HH:mm"),
            LaneId = summary.LaneId ?? "",
        };
    }

    /// <summary>把历史消息投成聊天气泡；工具结果按 callId 并回调用行。</summary>
    /// <param name="messages">按 id 升序的消息。</param>
    public static IReadOnlyList<ChatMessageItem> ToChatItems(IReadOnlyList<MessageView> messages)
    {
        var items = new List<ChatMessageItem>(messages.Count);
        var tools = new Dictionary<string, ChatMessageItem>(StringComparer.Ordinal);

        foreach (var message in messages)
        {
            var time = message.CreatedAt.ToLocalTime().ToString("HH:mm");
            var attachments = ToAttachments(message.Attachments);

            switch (message.Kind)
            {
                case MessageKind.ToolCall:
                {
                    // 工具调用前模型说的话（过程正文）是独立的一段话：历史里它和调用存在同一条消息里，
                    // 这里拆成「正文气泡 + 工具过程行」，刷新/重开对话后和流式时看到的一致。
                    if (!string.IsNullOrWhiteSpace(message.Text))
                    {
                        items.Add(new ChatMessageItem
                        {
                            Kind = ChatEntryKind.Assistant,
                            Text = message.Text,
                            Time = time,
                            Attachments = attachments,
                        });
                    }

                    var item = new ChatMessageItem
                    {
                        Kind = ChatEntryKind.Tool,
                        // 正文已经拆成上面的气泡；过程行只放调用参数 / 结果，避免重复
                        Text = "",
                        ToolName = message.ToolName,
                        ToolCallId = message.ToolCallId,
                        Detail = message.ToolArguments ?? "",
                        Time = time,
                    };
                    items.Add(item);
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
                        break;
                    }

                    items.Add(new ChatMessageItem
                    {
                        Kind = ChatEntryKind.Tool,
                        Text = message.Text,
                        ToolName = message.ToolName,
                        ToolCallId = message.ToolCallId,
                        Result = message.ToolResult ?? "",
                        Time = time,
                    });
                    break;
                }

                case MessageKind.SystemInput:
                {
                    items.Add(new ChatMessageItem
                    {
                        Kind = ChatEntryKind.SystemInput,
                        Text = message.Text,
                        Time = time,
                        Attachments = attachments,
                    });
                    break;
                }

                case MessageKind.Summary:
                {
                    items.Add(new ChatMessageItem
                    {
                        Kind = ChatEntryKind.System,
                        Text = message.Text,
                        Time = time,
                    });
                    break;
                }

                default:
                {
                    // 工作模式的第一条 system 是冻结的开场提示词，不进对话气泡（要看走 session.snapshot）
                    if (message.Role == MessageRole.System)
                    {
                        break;
                    }

                    if (!string.IsNullOrWhiteSpace(message.Thinking))
                    {
                        items.Add(new ChatMessageItem
                        {
                            Kind = ChatEntryKind.Reasoning,
                            Text = ConversationText.ClampReasoning(message.Thinking),
                            Time = time,
                        });
                    }

                    if (message.Role == MessageRole.User && ConversationText.IsSystemInjection(message.Text))
                    {
                        // 旧数据（或导入数据）里的系统注入没有独立种类，靠内容识别；照样折叠 + 默认隐藏。
                        items.Add(new ChatMessageItem
                        {
                            Kind = ChatEntryKind.SystemInput,
                            Text = message.Text,
                            Time = time,
                            Attachments = attachments,
                        });
                        break;
                    }

                    if (message.Role == MessageRole.Assistant && message.Kind == MessageKind.Text)
                    {
                        // 助手正文里的本地文件链接独立成泡：一个文件一个气泡，前后文字各成一段
                        items.AddRange(ToBubbleItems(message.Text, time, attachments));
                        break;
                    }

                    var kind = message.Role switch
                    {
                        MessageRole.User => ChatEntryKind.User,
                        MessageRole.Assistant => ChatEntryKind.Assistant,
                        MessageRole.Tool => ChatEntryKind.Tool,
                        _ => ChatEntryKind.System,
                    };

                    items.Add(new ChatMessageItem
                    {
                        Kind = kind,
                        Text = message.Text,
                        Time = time,
                        ToolName = kind == ChatEntryKind.Tool ? message.ToolName : null,
                        Attachments = attachments,
                    });
                    break;
                }
            }
        }

        return items;
    }

    /// <summary>
    /// 助手正文 → 气泡序列：本地文件链接拆成独立文件气泡（图片走图片气泡），
    /// 其余文字各成一段。<see cref="MessageAttachment"/> 挂在最后一个正文气泡上，
    /// 正文里只有文件链接时补一个只放附件的空正文气泡。
    /// </summary>
    /// <param name="text">助手正文。</param>
    /// <param name="time">时间文本。</param>
    /// <param name="attachments">附件卡片。</param>
    public static IReadOnlyList<ChatMessageItem> ToBubbleItems(
        string text,
        string time,
        IReadOnlyList<AttachmentItem>? attachments = null)
    {
        var bubbles = LocalFileLinkParser.Split(text);
        if (bubbles.Count == 0)
        {
            return
            [
                new ChatMessageItem
                {
                    Kind = ChatEntryKind.Assistant,
                    Text = text,
                    Time = time,
                    Attachments = attachments ?? [],
                },
            ];
        }

        if (bubbles.All(static bubble => !bubble.IsFile))
        {
            return
            [
                new ChatMessageItem
                {
                    Kind = ChatEntryKind.Assistant,
                    Text = text,
                    Time = time,
                    Attachments = attachments ?? [],
                },
            ];
        }

        var items = new List<ChatMessageItem>(bubbles.Count + 1);
        foreach (var bubble in bubbles)
        {
            if (!bubble.IsFile)
            {
                items.Add(new ChatMessageItem
                {
                    Kind = ChatEntryKind.Assistant,
                    Text = bubble.Text,
                    Time = time,
                });
                continue;
            }

            var path = bubble.FilePath!;
            var card = LocalFileCard.FromPath(path, bubble.FileLabel);
            var isImage = card.Kind == AttachmentKind.Image && File.Exists(path);
            items.Add(new ChatMessageItem
            {
                Kind = isImage ? ChatEntryKind.Image : ChatEntryKind.File,
                Time = time,
                ImageSource = isImage ? path : null,
                FilePath = path,
                FileCard = card,
            });
        }

        if (attachments is { Count: > 0 })
        {
            var target = items.LastOrDefault(static item => item.IsAssistant);
            if (target is null)
            {
                target = new ChatMessageItem
                {
                    Kind = ChatEntryKind.Assistant,
                    Time = time,
                };
                items.Add(target);
            }

            target.Attachments = attachments;
        }

        return items;
    }

    /// <summary>附件卡片。</summary>
    /// <param name="attachments">服务端下发的本地文件引用。</param>
    public static IReadOnlyList<AttachmentItem> ToAttachments(IReadOnlyList<MessageAttachment>? attachments)
    {
        if (attachments is null || attachments.Count == 0)
        {
            return [];
        }

        return attachments
            .Select(attachment =>
            {
                var item = new AttachmentItem
                {
                    Kind = attachment.Kind,
                    FileName = string.IsNullOrWhiteSpace(attachment.FileName)
                        ? Path.GetFileName(attachment.Path)
                        : attachment.FileName,
                    Meta = Describe(attachment),
                    Path = attachment.Path,
                };
                if (item.IsImage)
                {
                    _ = item.LoadImageAsync();
                }

                return item;
            })
            .ToList();
    }

    /// <summary>来源渠道的展示名。</summary>
    /// <param name="channel">渠道枚举。</param>
    public static string ChannelName(LaneChannel channel) => channel switch
    {
        LaneChannel.Desktop => "桌面",
        LaneChannel.Mattermost => "Mattermost",
        LaneChannel.WeChat => "微信",
        _ => "未知来源",
    };

    /// <summary>
    /// Lane 的展示名：渠道 + 模式（如 <c>Mattermost(角色)</c>）。
    /// 不拼角色名——同一个人可能同时挂在多个渠道上，名字已经由页面标题给出。
    /// </summary>
    /// <param name="channel">渠道枚举。</param>
    /// <param name="mode">工作 / 角色模式。</param>
    public static string LaneName(LaneChannel channel, Mode mode) => channel switch
    {
        LaneChannel.Desktop => "桌面",
        LaneChannel.Mattermost => mode == Mode.Roleplay ? "Mattermost(角色)" : "Mattermost(工作)",
        LaneChannel.WeChat => "微信(工作)",
        _ => ChannelName(channel),
    };

    private static string Describe(MessageAttachment attachment)
    {
        var size = attachment.SizeBytes switch
        {
            >= 1024 * 1024 => $"{attachment.SizeBytes / 1024d / 1024d:0.#} MB",
            >= 1024 => $"{attachment.SizeBytes / 1024d:0.#} KB",
            _ => $"{attachment.SizeBytes} B",
        };

        if (attachment.DurationMs is not > 0)
        {
            return size;
        }

        var duration = TimeSpan.FromMilliseconds(attachment.DurationMs.Value);
        return $"{size} · {(int)duration.TotalMinutes}:{duration.Seconds:00}";
    }
}
