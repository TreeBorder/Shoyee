using System.Text;
using System.Text.RegularExpressions;

namespace Shoyee.Desktop.Conversations;

/// <summary>拆出来的一个气泡：正文段，或一个本地文件引用。</summary>
/// <param name="Text">正文（文件气泡为空）。</param>
/// <param name="FilePath">本地文件绝对路径；正文段为 null。</param>
/// <param name="FileLabel">链接文字（可能为空，展示时回落文件名）。</param>
public readonly record struct MessageBubble(string Text, string? FilePath = null, string? FileLabel = null)
{
    /// <summary>是否文件气泡。</summary>
    public bool IsFile => FilePath is not null;
}

/// <summary>
/// 把助手正文按「本地文件链接」拆成多个气泡：一个文件一个气泡，链接前后的文字各成一段。
///
/// 只认三种写法，其余一律留在正文里（不猜）：
/// <list type="bullet">
///   <item><c>[标签](file:///绝对路径)</c>；</item>
///   <item><c>[标签](/绝对路径)</c> / <c>[标签](~/…)</c>，且文件确实存在；</item>
///   <item><c>![说明](本地图片路径)</c>。</item>
/// </list>
/// 相对路径不支持（桌面拿不到可靠基准），<c>http(s)</c> 链接也不算本地文件；
/// 代码围栏里的内容整段当正文，避免把示例路径拆成气泡。
/// </summary>
public static partial class LocalFileLinkParser
{
    /// <summary>拆分正文。</summary>
    /// <param name="text">助手正文（Markdown）。</param>
    /// <returns>按出现顺序排列的气泡；没有本地文件引用时只有一段正文。</returns>
    public static IReadOnlyList<MessageBubble> Split(string? text)
    {
        var bubbles = new List<MessageBubble>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return bubbles;
        }

        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var pending = new StringBuilder();

        void FlushText()
        {
            var body = pending.ToString().Trim();
            pending.Clear();
            if (body.Length > 0)
            {
                bubbles.Add(new MessageBubble(body));
            }
        }

        foreach (var (segment, isCode) in SliceCodeFences(normalized))
        {
            if (isCode)
            {
                pending.Append(segment);
                continue;
            }

            var cursor = 0;
            foreach (Match match in MarkdownLinkRegex().Matches(segment))
            {
                var target = match.Groups["angle"].Success
                    ? match.Groups["angle"].Value
                    : match.Groups["target"].Value;

                if (!TryResolveLocalPath(target, out var path))
                {
                    continue;
                }

                pending.Append(segment, cursor, match.Index - cursor);
                FlushText();

                var label = match.Groups["label"].Value.Trim();
                bubbles.Add(new MessageBubble("", path, label.Length == 0 ? null : label));
                cursor = match.Index + match.Length;
            }

            pending.Append(segment, cursor, segment.Length - cursor);
        }

        FlushText();
        return bubbles;
    }

    /// <summary>
    /// 把 Markdown 链接目标解析成本地绝对路径。
    /// <c>file://</c> 显式声明，不要求文件当下存在；裸绝对路径 / <c>~/</c> 要求文件存在，
    /// 避免把 <c>[路由](/api/users)</c> 这类普通链接误判成文件。
    /// </summary>
    /// <param name="target">链接括号里的内容。</param>
    /// <param name="path">解析出的绝对路径。</param>
    public static bool TryResolveLocalPath(string? target, out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(target))
        {
            return false;
        }

        var value = target.Trim();
        if (value.Length > 2 && value[0] == '<' && value[^1] == '>')
        {
            value = value[1..^1].Trim();
        }

        if (value.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "file")
            {
                return false;
            }

            path = uri.LocalPath;
            return path.Length > 0;
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute) && !absolute.IsFile)
        {
            // http(s)、mailto 等：不是本地文件（Unix 下以 / 开头的路径会被当成 file URI）
            return false;
        }

        if (value.StartsWith("~/", StringComparison.Ordinal))
        {
            value = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                value[2..]);
        }

        if (!Path.IsPathRooted(value))
        {
            return false;
        }

        try
        {
            var full = Path.GetFullPath(value);
            if (!File.Exists(full))
            {
                return false;
            }

            path = full;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    /// <summary>按 <c>```</c> 把文本切成「正文 / 代码块」两段交替。</summary>
    private static IEnumerable<(string Text, bool IsCode)> SliceCodeFences(string text)
    {
        var index = 0;
        while (index < text.Length)
        {
            var start = text.IndexOf("```", index, StringComparison.Ordinal);
            if (start < 0)
            {
                yield return (text[index..], false);
                yield break;
            }

            if (start > index)
            {
                yield return (text[index..start], false);
            }

            var end = text.IndexOf("```", start + 3, StringComparison.Ordinal);
            var fenceEnd = end < 0 ? text.Length : end + 3;
            yield return (text[start..fenceEnd], true);
            index = fenceEnd;
        }
    }

    [GeneratedRegex(
        @"(?<image>!)?\[(?<label>[^\]]*)\]\(\s*(?:<(?<angle>[^>]+)>|(?<target>[^)\s]+))(?:\s+(?:""[^""]*""|'[^']*'))?\s*\)")]
    private static partial Regex MarkdownLinkRegex();
}
