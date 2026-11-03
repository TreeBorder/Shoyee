namespace Shoyee.Tools.Files;

/// <summary>
/// 行编辑助手（移植自框架 <c>FileEditor</c>，MIT；语义原样保留）：供
/// <c>file_access_read_lines</c> / <c>file_access_replace</c> / <c>file_access_replace_lines</c> 使用，
/// 「行」的定义与 grep 报的行号一致（<see cref="SplitLinesKeepEnds"/>）。
/// </summary>
public static class FileEditor
{
    /// <summary>精确文本替换；oldString 找不到或不唯一（且未全换）时抛 <see cref="ArgumentException"/>。</summary>
    public static (string Content, int Count) ApplyReplace(string content, string oldString, string newString, bool replaceAll)
    {
        if (string.IsNullOrEmpty(oldString))
        {
            throw new ArgumentException("oldString 不能是空串。");
        }

        int count = CountOccurrences(content, oldString);
        if (count == 0)
        {
            throw new ArgumentException($"oldString 没找到：'{oldString}'（先 grep / read 确认真实文本再改）。");
        }

        if (count > 1 && !replaceAll)
        {
            throw new ArgumentException(
                $"oldString 命中 {count} 处；要么加 replaceAll=true 全换，要么把 oldString 写得更具体（多带上下文）。");
        }

        return (content.Replace(oldString, newString, StringComparison.Ordinal), count);
    }

    /// <summary>按 1-based 行号整行替换；空 new_line 删除该行（含换行）。</summary>
    public static string ApplyReplaceLines(string content, IReadOnlyList<FileLineEdit> edits)
    {
        if (edits.Count == 0)
        {
            throw new ArgumentException("至少要有一条行编辑。");
        }

        List<string> lines = SplitLinesKeepEnds(content);

        var seen = new HashSet<int>();
        foreach (var edit in edits)
        {
            if (!seen.Add(edit.LineNumber))
            {
                throw new ArgumentException($"edits 里 line_number {edit.LineNumber} 重复了。");
            }

            if (edit.LineNumber < 1 || edit.LineNumber > lines.Count)
            {
                throw new ArgumentException(
                    $"line_number {edit.LineNumber} 越界（文件共 {lines.Count} 行）。重新 read_lines 确认行号。");
            }

            if (edit.ExpectedLine is not null)
            {
                string actual = TrimLineTerminator(lines[edit.LineNumber - 1]);
                if (!string.Equals(actual, TrimLineTerminator(edit.ExpectedLine), StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        $"line_number {edit.LineNumber} 的内容和 expected_line 对不上（行号过期或文件变了）。"
                        + "重新 read_lines 拿当前行号再改。");
                }
            }
        }

        foreach (var edit in edits)
        {
            lines[edit.LineNumber - 1] = edit.NewLine;
        }

        return string.Concat(lines);
    }

    /// <summary>取 1-based 闭区间 <c>[startLine, endLine]</c> 的行（保留行尾）；endLine 越界自动截断。</summary>
    public static List<string> SliceLines(string content, int startLine, int? endLine)
    {
        List<string> lines = SplitLinesKeepEnds(content);
        int total = lines.Count;

        if (startLine < 1)
        {
            throw new ArgumentException($"startLine 必须是正整数，收到 {startLine}。");
        }

        if (endLine is < 1)
        {
            throw new ArgumentException($"endLine 必须是正整数，收到 {endLine}。");
        }

        if (endLine < startLine)
        {
            throw new ArgumentException($"endLine（{endLine}）不能小于 startLine（{startLine}）。");
        }

        if (startLine > total)
        {
            throw new ArgumentException($"startLine {startLine} 越界（文件共 {total} 行）。");
        }

        int lastLine = endLine is null ? total : Math.Min(endLine.Value, total);
        return lines.GetRange(startLine - 1, lastLine - startLine + 1);
    }

    /// <summary>去掉行尾的 <c>\r\n</c> / <c>\n</c> / 单独的 <c>\r</c>。</summary>
    public static string TrimLineTerminator(string line) => line[..LineContentLength(line)];

    /// <summary>行内容的长度（不含行尾终止符）。</summary>
    public static int LineContentLength(string line)
    {
        if (line.EndsWith("\r\n", StringComparison.Ordinal))
        {
            return line.Length - 2;
        }

        return line.EndsWith('\n') || line.EndsWith('\r') ? line.Length - 1 : line.Length;
    }

    /// <summary>按行切分并保留行尾（最后一行没有换行就不补）。</summary>
    public static List<string> SplitLinesKeepEnds(string content)
    {
        var lines = new List<string>();
        int start = 0;
        for (int i = 0; i < content.Length; i++)
        {
            char c = content[i];
            if (c == '\n')
            {
                lines.Add(content.Substring(start, i - start + 1));
                start = i + 1;
            }
            else if (c == '\r')
            {
                int end = i + 1 < content.Length && content[i + 1] == '\n' ? i + 2 : i + 1;
                lines.Add(content.Substring(start, end - start));
                i = end - 1;
                start = end;
            }
        }

        if (start < content.Length)
        {
            lines.Add(content[start..]);
        }

        return lines;
    }

    private static int CountOccurrences(string content, string value)
    {
        int count = 0;
        int index = 0;
        while ((index = content.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
