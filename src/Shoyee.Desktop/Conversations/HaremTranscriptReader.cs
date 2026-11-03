using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Shoyee.Contracts.Domain;
using Shoyee.Desktop.ViewModels.Items;

namespace Shoyee.Desktop.Conversations;

/// <summary>角色模式内容：从 Harem 的 rounds.json / state.json 解析出的时间线与状态。</summary>
/// <param name="CharacterName">角色名。</param>
/// <param name="SceneTitle">场景标题。</param>
/// <param name="SceneDetail">场景细节。</param>
/// <param name="LaneName">当前 Lane 展示名。</param>
/// <param name="States">悬浮状态。</param>
/// <param name="Messages">统一时间线。</param>
public sealed record RoleplayContent(
    string CharacterName,
    string SceneTitle,
    string SceneDetail,
    string LaneName,
    IReadOnlyList<StateItem> States,
    IReadOnlyList<ChatMessageItem> Messages);

/// <summary>把 Harem 的原始轮次读成 RP 时间线。</summary>
internal static partial class HaremTranscriptReader
{
    private static readonly IReadOnlyDictionary<string, string> SceneNames =
        new Dictionary<string, string>
        {
            ["studio"] = "摄影棚",
            ["bedroom"] = "卧室",
            ["living_room"] = "客厅",
        };

    private static readonly IReadOnlyDictionary<string, string> EmotionNames =
        new Dictionary<string, string>
        {
            ["happy"] = "愉快",
            ["calm"] = "平静",
            ["sad"] = "低落",
            ["angry"] = "生气",
        };

    /// <summary>读取随程序打包的示例快照。</summary>
    /// <returns>角色名、场景、状态与时间线。</returns>
    public static RoleplayContent Read() => Read(SampleTranscriptSource.LoadRoleplay());

    /// <summary>读取一份 Harem 原始轮次。</summary>
    /// <param name="raw">rounds.json + state.json 的内容。</param>
    /// <returns>角色名、场景、状态与时间线。</returns>
    internal static RoleplayContent Read(RoleplayRawFile raw)
    {
        var states = raw.State.ToDictionary(item => item.Key, item => item.Value);

        var messages = new List<ChatMessageItem>();
        var currentDate = "";
        var currentClock = "";

        foreach (var round in raw.Rounds)
        {
            if (!round.TryGetProperty("Request", out var requests)
                || requests.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var requestText = ReadRequestText(requests);
            var dateMatch = DateRegex().Match(requestText);
            var roundDate = dateMatch.Success ? dateMatch.Groups[1].Value : currentDate;
            if (roundDate.Length > 0 && roundDate != currentDate)
            {
                messages.Add(new ChatMessageItem
                {
                    Kind = ChatEntryKind.System,
                    Text = DateLabel(roundDate),
                });
                currentDate = roundDate;
            }

            foreach (var request in requests.EnumerateArray())
            {
                var rawText = ReadContentsText(request);
                var timeMatch = TimeRegex().Match(rawText);
                if (timeMatch.Success)
                {
                    currentClock = timeMatch.Groups[1].Value;
                }

                // [系统消息] 是内部注入的提示词，不在对话页展示。
                if (SystemMessageRegex().IsMatch(rawText))
                {
                    continue;
                }

                var user = UserMessageRegex().Match(rawText);
                if (user.Success)
                {
                    messages.Add(new ChatMessageItem
                    {
                        Kind = ChatEntryKind.User,
                        Text = ConversationText.ToPlainText(
                            ConversationText.CleanHarem(user.Groups[1].Value)),
                        Time = currentClock,
                        LaneName = "桌面",
                    });
                }
            }

            if (!round.TryGetProperty("Response", out var responses)
                || responses.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            // 出图和报备常常分在两条 assistant 消息里：图先挂着，等正文到达再一起显示。
            var pendingPhotos = new List<AttachmentItem>();
            var toolsByCallId = new Dictionary<string, ChatMessageItem>(StringComparer.Ordinal);
            ChatMessageItem? lastTool = null;
            foreach (var response in responses.EnumerateArray())
            {
                if (!response.TryGetProperty("Role", out var role))
                {
                    continue;
                }

                if (role.GetString() == "assistant")
                {
                    ReadAssistant(
                        response,
                        currentClock,
                        messages,
                        pendingPhotos,
                        toolsByCallId,
                        ref lastTool);
                }
                else if (role.GetString() == "tool")
                {
                    ReadToolResults(response, toolsByCallId, lastTool);
                }
            }
        }

        var scene = states.GetValueOrDefault("scene", "");
        var emotion = states.GetValueOrDefault("emotion", "");
        return new RoleplayContent(
            CharacterName: string.IsNullOrWhiteSpace(raw.AgentName) ? "筱义" : raw.AgentName,
            SceneTitle: SceneNames.GetValueOrDefault(scene, string.IsNullOrWhiteSpace(scene) ? "摄影棚" : scene),
            SceneDetail: "素背景 · 立绘",
            LaneName: "桌面",
            States:
            [
                new StateItem
                {
                    Name = "思念",
                    Value = Number(states, "longing").ToString(CultureInfo.InvariantCulture),
                    Percent = Number(states, "longing"),
                    IsNumeric = true,
                    IsGold = true,
                },
                new StateItem
                {
                    Name = "寂寞",
                    Value = Number(states, "loneliness").ToString(CultureInfo.InvariantCulture),
                    Percent = Number(states, "loneliness"),
                    IsNumeric = true,
                    IsMint = true,
                },
                new StateItem
                {
                    Name = "心情",
                    Value = EmotionNames.GetValueOrDefault(emotion, emotion),
                    IsNumeric = false,
                },
            ],
            Messages: messages);
    }

    private static void ReadAssistant(
        JsonElement response,
        string clock,
        List<ChatMessageItem> messages,
        List<AttachmentItem> pendingPhotos,
        Dictionary<string, ChatMessageItem> toolsByCallId,
        ref ChatMessageItem? lastTool)
    {
        if (!response.TryGetProperty("Contents", out var contents)
            || contents.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var text = new StringBuilder();

        void Flush()
        {
            var raw = text.ToString();
            text.Clear();
            if (string.IsNullOrWhiteSpace(raw))
            {
                // 工具调用可能排在正文前面：先留着照片，等正文到达再一起显示。
                return;
            }

            // 每个换行 / <br/> 拆成多个气泡，Markdown 图片独立成泡。
            var bubbles = RoleplayBubbleParser.Split(raw);
            if (bubbles.Count == 0)
            {
                return;
            }

            var attachments = pendingPhotos.ToList();
            pendingPhotos.Clear();

            messages.Add(new ChatMessageItem
            {
                Kind = bubbles[0].IsImage ? ChatEntryKind.Image : ChatEntryKind.Assistant,
                Text = bubbles[0].Text,
                ImageSource = bubbles[0].ImageSource,
                Time = clock,
                LaneName = "桌面",
                Attachments = attachments,
            });

            for (var i = 1; i < bubbles.Count; i++)
            {
                messages.Add(new ChatMessageItem
                {
                    Kind = bubbles[i].IsImage ? ChatEntryKind.Image : ChatEntryKind.Assistant,
                    Text = bubbles[i].Text,
                    ImageSource = bubbles[i].ImageSource,
                    Time = clock,
                    LaneName = "桌面",
                });
            }
        }

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
                            Time = clock,
                            LaneName = "桌面",
                        });
                    }

                    break;

                case "functionCall":
                    var name = ConversationText.ReadName(content);
                    var arguments = ConversationText.ReadArguments(content);
                    if (name is "camera" or "gallery")
                    {
                        pendingPhotos.Add(new AttachmentItem
                        {
                            Kind = AttachmentKind.Image,
                            FileName = $"{name}-{clock.Replace(":", "")}.jpg",
                            Meta = "AI 生成" + AspectRatio(arguments),
                        });
                    }

                    Flush();
                    var callId = ConversationText.ReadCallId(content);
                    var tool = new ChatMessageItem
                    {
                        Kind = ChatEntryKind.Tool,
                        ToolName = name,
                        ToolCallId = callId,
                        Text = ConversationText.DescribeTool(name, arguments),
                        Detail = ConversationText.FormatArguments(arguments),
                        Time = clock,
                        LaneName = "桌面",
                    };
                    messages.Add(tool);
                    lastTool = tool;
                    if (callId.Length > 0)
                    {
                        toolsByCallId[callId] = tool;
                    }

                    break;
            }
        }

        Flush();
    }

    private static void ReadToolResults(
        JsonElement response,
        Dictionary<string, ChatMessageItem> toolsByCallId,
        ChatMessageItem? lastTool)
    {
        if (!response.TryGetProperty("Contents", out var contents)
            || contents.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var content in contents.EnumerateArray())
        {
            if (!content.TryGetProperty("$type", out var kind)
                || kind.GetString() != "functionResult")
            {
                continue;
            }

            var callId = ConversationText.ReadCallId(content);
            var target = callId.Length > 0 && toolsByCallId.TryGetValue(callId, out var call)
                ? call
                : lastTool;

            if (target is not null)
            {
                target.Result = ConversationText.ReadResult(content);
            }
        }
    }

    private static string AspectRatio(JsonElement? arguments)
    {
        if (arguments is not { ValueKind: JsonValueKind.Object } args
            || !args.TryGetProperty("aspectRatio", out var ratio)
            || ratio.ValueKind != JsonValueKind.String)
        {
            return "";
        }

        return $" · {ratio.GetString()?.Replace(':', '×')}";
    }

    private static string ReadRequestText(JsonElement requests)
    {
        var text = new StringBuilder();
        foreach (var request in requests.EnumerateArray())
        {
            text.Append(ReadContentsText(request));
        }

        return text.ToString();
    }

    private static string ReadContentsText(JsonElement message)
    {
        if (!message.TryGetProperty("Contents", out var contents)
            || contents.ValueKind != JsonValueKind.Array)
        {
            return "";
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

    private static string DateLabel(string value)
    {
        if (!DateTime.TryParseExact(
                value,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
        {
            return value;
        }

        string[] weekdays = ["周日", "周一", "周二", "周三", "周四", "周五", "周六"];
        return $"{date:MM-dd} · {weekdays[(int)date.DayOfWeek]}";
    }

    private static int Number(IReadOnlyDictionary<string, string> state, string key) =>
        int.TryParse(state.GetValueOrDefault(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;

    [GeneratedRegex(@"消息时间:\s*(20\d{2}-\d{2}-\d{2})")]
    private static partial Regex DateRegex();

    [GeneratedRegex(@"消息时间:[^\]]*?(\d{1,2}:\d{2})")]
    private static partial Regex TimeRegex();

    [GeneratedRegex(@"<SystemMessage>(.*?)</SystemMessage>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex SystemMessageRegex();

    [GeneratedRegex(@"<UserMessage>(.*?)</UserMessage>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex UserMessageRegex();
}
