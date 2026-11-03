using System.Text;
using System.Text.RegularExpressions;

namespace Shoyee.Contracts.Text;

/// <summary>
/// RP 正文按「单个换行」切段：模型里一个换行就是一条消息（气泡）。
/// 代码块内部不切——围栏标记和代码行保持在同一段里，避免把代码拆成几条消息。
/// <c>&lt;br/&gt;</c> 与换行等价；空行不产生空消息。
/// </summary>
public static partial class ReplySegments
{
    /// <summary>把一段 RP 正文切成要逐条发送的片段。</summary>
    /// <param name="text">清洗后的正文（通常已过 <see cref="MessageText.CleanReply"/>）。</param>
    /// <returns>非空片段；原文没有内容时返回空列表。</returns>
    public static IReadOnlyList<string> Split(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var normalized = BreakTag().Replace(text, "\n")
            .Replace("\r\n", "\n")
            .Replace('\r', '\n');

        var segments = new List<string>();
        var current = new StringBuilder();
        var inFence = false;

        foreach (var line in normalized.Split('\n'))
        {
            var stripped = line.TrimStart();
            if (inFence)
            {
                Append(current, line);
                if (stripped.StartsWith("```", StringComparison.Ordinal))
                {
                    inFence = false;
                    Flush(current, segments);
                }

                continue;
            }

            if (stripped.StartsWith("```", StringComparison.Ordinal))
            {
                Flush(current, segments);
                Append(current, line);
                inFence = true;
                continue;
            }

            Append(current, line);
            Flush(current, segments);
        }

        Flush(current, segments);
        return segments;
    }

    private static void Append(StringBuilder builder, string line)
    {
        if (builder.Length > 0)
        {
            builder.Append('\n');
        }

        builder.Append(line);
    }

    private static void Flush(StringBuilder builder, List<string> segments)
    {
        if (builder.Length == 0)
        {
            return;
        }

        var segment = builder.ToString().Trim();
        builder.Clear();
        if (segment.Length > 0)
        {
            segments.Add(segment);
        }
    }

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex BreakTag();
}
