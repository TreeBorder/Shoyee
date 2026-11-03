using System.Text.Json;
using System.Text.RegularExpressions;
using Shoyee.Contracts.Domain;
using Shoyee.Desktop.ViewModels.Items;

namespace Shoyee.Desktop.Conversations;

/// <summary>把源项目的文本约定转换成桌面端能显示的内容。</summary>
internal static partial class ConversationText
{
    private static readonly JsonSerializerOptions PrettyJsonOptions = new() { WriteIndented = true };

    private static readonly HashSet<string> ImageExtensions =
        [".jpg", ".jpeg", ".png", ".gif", ".webp", ".heic"];

    private static readonly HashSet<string> AudioExtensions =
        [".silk", ".mp3", ".wav", ".m4a", ".ogg", ".flac"];

    private static readonly HashSet<string> VideoExtensions =
        [".mp4", ".mov", ".mkv", ".webm"];

    /// <summary>清理 Harem 输出：去掉 InnerVoice、内部标签和敏感词包裹符。</summary>
    /// <param name="text">原始文本。</param>
    /// <returns>可直接展示的正文。</returns>
    public static string CleanHarem(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        var cleaned = InnerVoiceRegex().Replace(text, "");
        cleaned = TagRegex().Replace(cleaned, "").Replace("@", "");
        cleaned = BlankLinesRegex().Replace(cleaned, "\n\n");
        return cleaned.Trim();
    }

    /// <summary>清理系统注入消息：Harem 用 <c>[定时任务系统消息]…[/]</c> 包一层。</summary>
    /// <param name="text">原始系统消息。</param>
    /// <returns>正文。</returns>
    public static string CleanSystem(string text)
    {
        var cleaned = LeadingBracketTagRegex().Replace(text, "");
        return CleanHarem(cleaned.Replace("[/]", ""));
    }

    /// <summary>
    /// 历史消息是否是系统注入（旧数据没有 <see cref="MessageKind.SystemInput"/> 种类，
    /// 靠内容前缀识别，桌面默认照样隐藏）。
    /// </summary>
    /// <param name="text">消息正文。</param>
    public static bool IsSystemInjection(string text)
        => text.Contains("<SystemMessage>", StringComparison.OrdinalIgnoreCase)
           || text.Contains("[系统消息]", StringComparison.Ordinal)
           || text.Contains("[定时任务系统消息]", StringComparison.Ordinal);

    /// <summary>把 Markdown 压成纯文本：桌面端目前是 TextBlock，不渲染 Markdown 标记。</summary>
    /// <param name="text">Markdown 文本。</param>
    /// <returns>去掉标题、强调、代码围栏和链接语法后的文本。</returns>
    public static string ToPlainText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text ?? "";
        }

        var plain = CodeFenceRegex().Replace(text, "");
        plain = ImageRegex().Replace(plain, "$1");
        plain = LinkRegex().Replace(plain, "$1");
        plain = BoldRegex().Replace(plain, "$1");
        plain = InlineCodeRegex().Replace(plain, "$1");
        plain = HeadingRegex().Replace(plain, "");
        plain = QuoteRegex().Replace(plain, "");
        plain = BulletRegex().Replace(plain, "• ");
        plain = ThematicBreakRegex().Replace(plain, "———");
        plain = ItalicRegex().Replace(plain, "$1");
        return BlankLinesRegex().Replace(plain, "\n\n").Trim();
    }

    /// <summary>拆出用户正文与附件卡片；附件说明是 Secretary 自己写的固定格式。</summary>
    /// <param name="text">用户消息原文。</param>
    /// <returns>正文和附件。</returns>
    public static (string Body, IReadOnlyList<AttachmentItem> Attachments) SplitUserText(string text)
    {
        var attachments = new List<AttachmentItem>();
        foreach (Match block in AttachmentBlockRegex().Matches(text))
        {
            foreach (var line in block.Groups[1].Value.Split('\n'))
            {
                var match = AttachmentLineRegex().Match(line);
                if (!match.Success)
                {
                    continue;
                }

                var fileName = match.Groups[1].Value.Trim();
                attachments.Add(new AttachmentItem
                {
                    Kind = KindOf(fileName),
                    FileName = fileName,
                    Meta = MetaOf(fileName),
                });
            }
        }

        var body = AttachmentBlockRegex().Replace(text, "");
        body = body.Replace("[微信语音转写]", "");
        body = TranscriptLineRegex().Replace(body, "");
        body = BlankLinesRegex().Replace(body, "\n\n");
        return (body.Trim(), attachments);
    }

    /// <summary>把工具调用参数压成一行摘要。</summary>
    /// <param name="name">工具名。</param>
    /// <param name="arguments">参数对象；可以为空。</param>
    /// <returns>单行摘要。</returns>
    public static string DescribeTool(string name, JsonElement? arguments = null)
    {
        if (arguments is not { ValueKind: JsonValueKind.Object } args)
        {
            return "";
        }

        if (name == "terminal" && TryGetString(args, "command") is { Length: > 0 } command)
        {
            return Clip(command.Replace("\n", " ↵ "), 130);
        }

        var parts = new List<string>();
        foreach (var property in args.EnumerateObject())
        {
            if (property.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                continue;
            }

            var value = property.Value.ValueKind == JsonValueKind.String
                ? property.Value.GetString() ?? ""
                : property.Value.GetRawText();
            if (value.Length == 0)
            {
                continue;
            }

            parts.Add($"{property.Name}={value.Replace("\n", " ↵ ")}");
            if (string.Join(" · ", parts).Length > 130)
            {
                break;
            }
        }

        return Clip(string.Join(" · ", parts), 140);
    }

    /// <summary>读取 JSON 里的 <c>text</c> / <c>Text</c>。</summary>
    /// <param name="element">内容对象。</param>
    /// <returns>文本；没有则空串。</returns>
    public static string ReadText(JsonElement element) =>
        TryGetString(element, "text") ?? TryGetString(element, "Text") ?? "";

    /// <summary>读取 JSON 里的 <c>name</c> / <c>Name</c>。</summary>
    /// <param name="element">functionCall 对象。</param>
    /// <returns>工具名。</returns>
    public static string ReadName(JsonElement element) =>
        TryGetString(element, "name") ?? TryGetString(element, "Name") ?? "tool";

    /// <summary>读取 functionCall 的参数对象。</summary>
    /// <param name="element">functionCall 对象。</param>
    /// <returns>参数；没有则 null。</returns>
    public static JsonElement? ReadArguments(JsonElement element)
    {
        if (element.TryGetProperty("arguments", out var lower)
            || element.TryGetProperty("Arguments", out lower))
        {
            return lower.ValueKind == JsonValueKind.Object ? lower : null;
        }

        return null;
    }

    /// <summary>读取调用 / 结果的 callId。</summary>
    /// <param name="element">functionCall 或 functionResult 对象。</param>
    /// <returns>callId；没有则空串。</returns>
    public static string ReadCallId(JsonElement element) =>
        TryGetString(element, "callId")
        ?? TryGetString(element, "CallId")
        ?? TryGetString(element, "call_id")
        ?? "";

    /// <summary>读取工具返回结果；字符串原样，对象 / 数组转成缩进 JSON。</summary>
    /// <param name="element">functionResult 对象。</param>
    /// <returns>可展示的结果文本。</returns>
    public static string ReadResult(JsonElement element)
    {
        if (!element.TryGetProperty("result", out var result)
            && !element.TryGetProperty("Result", out result))
        {
            return "";
        }

        return result.ValueKind switch
        {
            JsonValueKind.String => result.GetString() ?? "",
            JsonValueKind.Null or JsonValueKind.Undefined => "",
            _ => PrettyJson(result),
        };
    }

    /// <summary>把工具参数格式化成缩进 JSON，供展开区展示。</summary>
    /// <param name="arguments">参数对象。</param>
    /// <returns>缩进后的 JSON；没有参数则空串。</returns>
    public static string FormatArguments(JsonElement? arguments) =>
        arguments is { } value ? PrettyJson(value) : "";

    private static string PrettyJson(JsonElement value)
    {
        try
        {
            return JsonSerializer.Serialize(value, PrettyJsonOptions);
        }
        catch (NotSupportedException)
        {
            return value.GetRawText();
        }
    }

    /// <summary>会话列表里的相对时间。</summary>
    /// <param name="value">时间。</param>
    /// <returns>今天显示时分，昨天显示「昨天」，更早显示月日。</returns>
    public static string FormatSessionTime(DateTimeOffset value)
    {
        var local = value.ToLocalTime();
        var today = DateTimeOffset.Now.Date;
        if (local.Date == today)
        {
            return local.ToString("HH:mm");
        }

        if (local.Date == today.AddDays(-1))
        {
            return "昨天";
        }

        return local.Year == DateTimeOffset.Now.Year
            ? local.ToString("MM-dd")
            : local.ToString("yyyy-MM-dd");
    }

    /// <summary>截断过长的预览文本。</summary>
    /// <param name="text">原文。</param>
    /// <param name="max">最大长度。</param>
    /// <returns>截断后的文本。</returns>
    public static string Clip(string text, int max) =>
        text.Length <= max ? text : text[..max] + "…";

    /// <summary>思考过程在桌面内存里保留的最大字符数（完整文本仍在库里）。</summary>
    public const int MaxReasoningChars = 40_000;

    /// <summary>截断过长的思考，避免一条几 MB 的思考撑爆 UI 文本与布局。</summary>
    /// <param name="text">思考原文。</param>
    /// <returns>可安全放进气泡的文本；被截断时末尾带一句说明。</returns>
    public static string ClampReasoning(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        return text.Length <= MaxReasoningChars
            ? text
            : text[..MaxReasoningChars] + "\n\n…（思考过长，后面的内容未展示）";
    }

    private static AttachmentKind KindOf(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (ImageExtensions.Contains(extension))
        {
            return AttachmentKind.Image;
        }

        if (AudioExtensions.Contains(extension))
        {
            return AttachmentKind.Audio;
        }

        return VideoExtensions.Contains(extension) ? AttachmentKind.Video : AttachmentKind.File;
    }

    private static string MetaOf(string fileName)
    {
        var kind = KindOf(fileName);
        var label = kind switch
        {
            AttachmentKind.Image => "图片",
            AttachmentKind.Audio => "语音",
            AttachmentKind.Video => "视频",
            _ => "文件",
        };
        return $"{label} · 收件箱";
    }

    private static string? TryGetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    [GeneratedRegex(@"<InnerVoice>.*?</InnerVoice>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex InnerVoiceRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"^\s*\[[^\]]+\]")]
    private static partial Regex LeadingBracketTagRegex();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex BlankLinesRegex();

    [GeneratedRegex(@"\[老板发来附件（已存本地，用 file_access 读）：\s*\n(.*?)\]", RegexOptions.Singleline)]
    private static partial Regex AttachmentBlockRegex();

    [GeneratedRegex(@"-\s*(.+?)\s*→\s*(.+)")]
    private static partial Regex AttachmentLineRegex();

    [GeneratedRegex(@"(?m)^\[语音转写\].*\n?")]
    private static partial Regex TranscriptLineRegex();

    [GeneratedRegex(@"(?m)^\s*```.*$")]
    private static partial Regex CodeFenceRegex();

    [GeneratedRegex(@"!\[([^\]]*)\]\([^)]*\)")]
    private static partial Regex ImageRegex();

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]*\)")]
    private static partial Regex LinkRegex();

    [GeneratedRegex(@"\*\*(.+?)\*\*", RegexOptions.Singleline)]
    private static partial Regex BoldRegex();

    [GeneratedRegex(@"`([^`]+)`")]
    private static partial Regex InlineCodeRegex();

    [GeneratedRegex(@"(?m)^#{1,6}\s*")]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"(?m)^>\s?")]
    private static partial Regex QuoteRegex();

    [GeneratedRegex(@"(?m)^[-*]\s+")]
    private static partial Regex BulletRegex();

    [GeneratedRegex(@"(?m)^---+$")]
    private static partial Regex ThematicBreakRegex();

    [GeneratedRegex(@"(?<!\*)\*(?!\*)(.+?)(?<!\*)\*(?!\*)")]
    private static partial Regex ItalicRegex();
}
