using System.Text;

namespace Shoyee.Web.DeepSeek;

/// <summary>
/// 反转义抓取后端给 Markdown 加的反斜杠转义（<c>\.</c>、<c>\:</c>、<c>\/</c>、<c>\-</c>…）。
/// 只处理「反斜杠 + ASCII 标点」，字母/数字/空白前的反斜杠原样保留。
/// </summary>
internal static class MarkdownEscapes
{
    /// <summary>去掉标点前的反斜杠；从左到右处理，<c>\\</c> 会折叠成一个 <c>\</c>。</summary>
    public static string Unescape(string content)
    {
        if (string.IsNullOrEmpty(content) || content.IndexOf('\\') < 0)
        {
            return content;
        }

        var builder = new StringBuilder(content.Length);
        for (var i = 0; i < content.Length; i++)
        {
            var current = content[i];
            if (current == '\\'
                && i + 1 < content.Length
                && IsEscapable(content[i + 1]))
            {
                builder.Append(content[++i]);
                continue;
            }

            builder.Append(current);
        }

        return builder.ToString();
    }

    private static bool IsEscapable(char c)
        => char.IsAscii(c) && !char.IsAsciiLetterOrDigit(c) && !char.IsWhiteSpace(c);
}
