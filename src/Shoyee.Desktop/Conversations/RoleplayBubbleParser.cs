using System.Text.RegularExpressions;

namespace Shoyee.Desktop.Conversations;

/// <summary>RP 气泡：一段正文，或一张 Markdown 图片。</summary>
/// <param name="Text">正文（已清理成纯文本）；图片气泡为空。</param>
/// <param name="ImageSource">图片地址（本地绝对路径 / <c>file://</c> / <c>http(s)</c>）；正文气泡为 null。</param>
public readonly record struct RpBubble(string Text, string? ImageSource)
{
    /// <summary>是否图片气泡。</summary>
    public bool IsImage => ImageSource is not null;
}

/// <summary>
/// 把一条助手回复拆成 RP 气泡。
///
/// 分段规则：<c>&lt;br/&gt;</c> 或**任意一个换行**各成一段（RP 里换行就是换气泡，
/// 微信渠道另行处理，但 RP 目前不接微信），代码块内部的换行不参与分段。
/// Markdown 图片 <c>![alt](url)</c> 一律独立成图片气泡，正文按原顺序排在图片前后。
/// </summary>
internal static partial class RoleplayBubbleParser
{
    /// <summary>拆分一条回复。</summary>
    /// <param name="raw">模型返回的原始正文。</param>
    /// <returns>按显示顺序排列的气泡。</returns>
    public static IReadOnlyList<RpBubble> Split(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        // Harem 用 <br/> 当分段符；CleanHarem 会把它当标签删掉，所以先换成换行。
        var text = BreakTagRegex().Replace(raw, "\n");
        text = ConversationText.CleanHarem(text);

        var bubbles = new List<RpBubble>();
        foreach (var paragraph in SplitParagraphs(text))
        {
            AppendParagraph(paragraph, bubbles);
        }

        return bubbles;
    }

    private static void AppendParagraph(string paragraph, List<RpBubble> bubbles)
    {
        var cursor = 0;
        foreach (Match image in ImageRegex().Matches(paragraph))
        {
            AppendText(paragraph[cursor..image.Index], bubbles);
            bubbles.Add(new RpBubble("", image.Groups["url"].Value.Trim()));
            cursor = image.Index + image.Length;
        }

        AppendText(paragraph[cursor..], bubbles);
    }

    private static void AppendText(string text, List<RpBubble> bubbles)
    {
        var plain = ConversationText.ToPlainText(text).Trim();
        if (plain.Length > 0)
        {
            bubbles.Add(new RpBubble(plain, null));
        }
    }

    /// <summary>
    /// 按换行分段（单换行也分段），代码块内整段保留。
    /// </summary>
    private static List<string> SplitParagraphs(string text)
    {
        var segments = new List<string>();
        var codeBlocks = new List<(int Start, int End)>();
        var index = 0;
        while (true)
        {
            var start = text.IndexOf("```", index, StringComparison.Ordinal);
            if (start < 0)
            {
                break;
            }

            var end = text.IndexOf("```", start + 3, StringComparison.Ordinal);
            if (end < 0)
            {
                codeBlocks.Add((start, text.Length));
                break;
            }

            codeBlocks.Add((start, end + 3));
            index = end + 3;
        }

        if (codeBlocks.Count == 0)
        {
            AddSegments(text, segments);
            return segments;
        }

        index = 0;
        foreach (var (start, end) in codeBlocks)
        {
            if (index < start)
            {
                AddSegments(text[index..start], segments);
            }

            segments.Add(text[start..end]);
            index = end;
        }

        if (index < text.Length)
        {
            AddSegments(text[index..], segments);
        }

        return segments;
    }

    private static void AddSegments(string text, List<string> segments)
    {
        foreach (var segment in ParagraphBreakRegex().Split(text))
        {
            if (!string.IsNullOrWhiteSpace(segment))
            {
                segments.Add(segment);
            }
        }
    }

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex BreakTagRegex();

    [GeneratedRegex(@"\r?\n")]
    private static partial Regex ParagraphBreakRegex();

    [GeneratedRegex(@"!\[[^\]]*\]\(\s*<?(?<url>[^)\s>]+)>?(?:\s+(?:""[^""]*""|'[^']*'))?\s*\)")]
    private static partial Regex ImageRegex();
}
