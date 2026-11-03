using System.Globalization;
using System.Text;
using System.Text.Json;
using Shoyee.Desktop.ViewModels.Items;

namespace Shoyee.Desktop.Conversations;

/// <summary>把 Secretary 的原始消息行读成会话列表与消息时间线。</summary>
internal static class SecretaryTranscriptReader
{
    /// <summary>读取工作模式快照。</summary>
    /// <returns>会话列表（按源数据的创建时间倒序）。</returns>
    public static IReadOnlyList<SessionItem> ReadSessions() =>
        SampleTranscriptSource.LoadWork()
            .Sessions
            .Select(ReadSession)
            .OrderByDescending(item => item.CreatedAt)
            .ToList();

    /// <summary>把一条原始会话读成列表项（导入器复用）。</summary>
    /// <param name="raw">原始会话。</param>
    /// <returns>会话列表项。</returns>
    internal static SessionItem ReadSession(WorkRawSession raw)
    {
        var messages = ReadMessages(raw);
        return new SessionItem
        {
            Title = raw.Title,
            Preview = ConversationText.Clip(
                raw.Messages.LastOrDefault(message => message.Role == "assistant") is { } last
                    ? ConversationText.ToPlainText(LastText(last)).Replace('\n', ' ')
                    : "",
                60),
            Time = ConversationText.FormatSessionTime(raw.LastActivityAt),
            Source = raw.Source,
            IsExternal = raw.External,
            IsPinned = raw.Pinned,
            IsArchived = raw.Archived,
            MessageCount = messages.Count,
            CreatedAt = raw.CreatedAt,
            CreatedText = raw.CreatedAt.ToLocalTime().ToString("MM-dd HH:mm"),
            LaneId = raw.LaneId,
            LastInputTokens = raw.LastInputTokens,
            LastCachedInputTokens = raw.LastCachedInputTokens,
            Messages = messages,
        };
    }

    private static IReadOnlyList<ChatMessageItem> ReadMessages(WorkRawSession raw)
    {
        var messages = new List<ChatMessageItem>
        {
            new()
            {
                Kind = ChatEntryKind.System,
                Text = raw.Messages.Count < raw.MessageCount
                    ? $"会话快照已冻结 · 更早的消息未载入（共 {raw.MessageCount} 条）"
                    : $"会话快照已冻结 · {raw.CreatedAt.ToLocalTime():MM-dd HH:mm}",
            },
        };

        // 工具返回在数据库里是独立的 tool 行，按 callId 对回前面那条 functionCall。
        var toolsByCallId = new Dictionary<string, ChatMessageItem>(StringComparer.Ordinal);
        ChatMessageItem? lastTool = null;

        foreach (var message in raw.Messages)
        {
            switch (message.Role)
            {
                case "user":
                    var (body, attachments) = ConversationText.SplitUserText(message.Content);
                    messages.Add(new ChatMessageItem
                    {
                        Kind = ChatEntryKind.User,
                        Text = ConversationText.ToPlainText(body),
                        Time = Clock(message.CreatedAt),
                        Attachments = attachments,
                    });
                    break;

                case "assistant":
                    ReadAssistant(message, messages, toolsByCallId, ref lastTool);
                    break;

                case "tool":
                    ReadToolResults(message, toolsByCallId, lastTool);
                    break;
            }
        }

        return messages;
    }

    private static void ReadAssistant(
        WorkRawMessage raw,
        List<ChatMessageItem> messages,
        Dictionary<string, ChatMessageItem> toolsByCallId,
        ref ChatMessageItem? lastTool)
    {
        if (string.IsNullOrWhiteSpace(raw.MessageJson))
        {
            AddAssistant(raw.Content, raw.CreatedAt, messages);
            return;
        }

        using var document = JsonDocument.Parse(raw.MessageJson);
        var root = document.RootElement;
        var createdAt = ReadCreatedAt(root) ?? raw.CreatedAt;
        var text = new StringBuilder();

        void Flush()
        {
            var body = text.ToString().Trim();
            text.Clear();
            if (body.Length > 0)
            {
                AddAssistant(body, createdAt, messages);
            }
        }

        if (root.TryGetProperty("contents", out var contents)
            && contents.ValueKind == JsonValueKind.Array)
        {
            foreach (var content in contents.EnumerateArray())
            {
                if (!content.TryGetProperty("$type", out var kind))
                {
                    continue;
                }

                switch (kind.GetString())
                {
                    case "text":
                        text.Append(ConversationText.ReadText(content));
                        break;

                    case "reasoning":
                        Flush();
                        var thought = ConversationText.ReadText(content).Trim();
                        if (thought.Length > 0)
                        {
                            messages.Add(new ChatMessageItem
                            {
                                Kind = ChatEntryKind.Reasoning,
                                Text = thought,
                                Time = Clock(createdAt),
                            });
                        }

                        break;

                    case "functionCall":
                        Flush();
                        var name = ConversationText.ReadName(content);
                        var arguments = ConversationText.ReadArguments(content);
                        var callId = ConversationText.ReadCallId(content);
                        var tool = new ChatMessageItem
                        {
                            Kind = ChatEntryKind.Tool,
                            ToolName = name,
                            ToolCallId = callId,
                            Text = ConversationText.DescribeTool(name, arguments),
                            Detail = ConversationText.FormatArguments(arguments),
                            Time = Clock(createdAt),
                        };
                        messages.Add(tool);
                        lastTool = tool;
                        if (callId.Length > 0)
                        {
                            toolsByCallId[callId] = tool;
                        }

                        break;

                    case "functionResult":
                        AttachResult(content, toolsByCallId, lastTool);
                        break;
                }
            }
        }

        Flush();
    }

    private static void ReadToolResults(
        WorkRawMessage raw,
        Dictionary<string, ChatMessageItem> toolsByCallId,
        ChatMessageItem? lastTool)
    {
        if (string.IsNullOrWhiteSpace(raw.MessageJson))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(raw.MessageJson);
            if (!document.RootElement.TryGetProperty("contents", out var contents)
                || contents.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var content in contents.EnumerateArray())
            {
                if (content.TryGetProperty("$type", out var kind)
                    && kind.GetString() == "functionResult")
                {
                    AttachResult(content, toolsByCallId, lastTool);
                }
            }
        }
        catch (JsonException)
        {
            // 结果行坏了不影响正文
        }
    }

    private static void AttachResult(
        JsonElement content,
        Dictionary<string, ChatMessageItem> toolsByCallId,
        ChatMessageItem? lastTool)
    {
        var callId = ConversationText.ReadCallId(content);
        var target = callId.Length > 0 && toolsByCallId.TryGetValue(callId, out var call)
            ? call
            : lastTool;

        if (target is not null)
        {
            target.Result = ConversationText.ReadResult(content);
        }
    }

    private static void AddAssistant(
        string text,
        DateTimeOffset createdAt,
        List<ChatMessageItem> messages)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        messages.Add(new ChatMessageItem
        {
            Kind = ChatEntryKind.Assistant,
            Text = ConversationText.ToPlainText(text),
            Time = Clock(createdAt),
        });
    }

    private static string LastText(WorkRawMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.MessageJson))
        {
            return message.Content;
        }

        try
        {
            using var document = JsonDocument.Parse(message.MessageJson);
            if (!document.RootElement.TryGetProperty("contents", out var contents)
                || contents.ValueKind != JsonValueKind.Array)
            {
                return message.Content;
            }

            var text = new StringBuilder();
            foreach (var content in contents.EnumerateArray())
            {
                if (content.TryGetProperty("$type", out var kind) && kind.GetString() == "text")
                {
                    text.Append(ConversationText.ReadText(content));
                }
            }

            return text.ToString();
        }
        catch (JsonException)
        {
            return message.Content;
        }
    }

    private static DateTimeOffset? ReadCreatedAt(JsonElement root) =>
        root.TryGetProperty("createdAt", out var value)
        && value.ValueKind == JsonValueKind.String
        && DateTimeOffset.TryParse(
            value.GetString(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? parsed
            : null;

    private static string Clock(DateTimeOffset value) => value.ToLocalTime().ToString("HH:mm");
}
