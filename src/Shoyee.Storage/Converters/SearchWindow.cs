namespace Shoyee.Storage.Converters;

/// <summary>围绕命中位置开窗取片段（FTS5 的 snippet() 在 trigram 下不可靠）。</summary>
public static class SearchWindow
{
    private const int DefaultWidth = 120;

    /// <summary>截取围绕首个命中位置的片段，两侧加省略号。</summary>
    public static string Cut(string content, string query, int width = DefaultWidth)
    {
        if (string.IsNullOrEmpty(content))
        {
            return string.Empty;
        }

        if (content.Length <= width)
        {
            return content;
        }

        var index = content.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            var firstToken = query.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (!string.IsNullOrEmpty(firstToken))
            {
                index = content.IndexOf(firstToken, StringComparison.OrdinalIgnoreCase);
            }
        }

        if (index < 0)
        {
            index = 0;
        }

        var start = Math.Max(0, index - width / 3);
        if (start + width > content.Length)
        {
            start = Math.Max(0, content.Length - width);
        }

        var length = Math.Min(width, content.Length - start);
        var snippet = content.Substring(start, length);
        var prefix = start > 0 ? "……" : string.Empty;
        var suffix = start + length < content.Length ? "……" : string.Empty;
        return prefix + snippet + suffix;
    }
}
