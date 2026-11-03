namespace Shoyee.Notify.ServerChan;

/// <summary>
/// 读取 shell 风格的凭据文件（<c>KEY=value</c> / <c>export KEY=value</c>，支持单双引号）。
/// 只解析需要的键，不执行脚本、不展开变量。
/// </summary>
internal static class ShellConfig
{
    /// <summary>解析文件为键值对；文件不存在时抛 <see cref="FileNotFoundException"/>。</summary>
    public static IReadOnlyDictionary<string, string> Read(string path)
    {
        var fullPath = ExpandHome(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"凭据文件不存在：{fullPath}", fullPath);
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in File.ReadAllLines(fullPath))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith("export ", StringComparison.Ordinal))
            {
                line = line[7..].TrimStart();
            }

            var equals = line.IndexOf('=');
            if (equals <= 0)
            {
                continue;
            }

            var key = line[..equals].Trim();
            if (!IsShellName(key))
            {
                continue;
            }

            values[key] = ParseValue(line[(equals + 1)..]);
        }

        return values;
    }

    /// <summary>展开开头的 <c>~/</c>。</summary>
    public static string ExpandHome(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return path.StartsWith("~/", StringComparison.Ordinal)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..])
            : path;
    }

    private static string ParseValue(string raw)
    {
        var value = raw.Trim();
        if (value.Length == 0)
        {
            return value;
        }

        var quote = value[0];
        if (quote is '"' or '\'')
        {
            var end = value.IndexOf(quote, 1);
            return end > 0 ? value[1..end] : value[1..];
        }

        // 未加引号时，行尾的空白 + # 视为注释
        var comment = value.IndexOf(" #", StringComparison.Ordinal);
        return (comment >= 0 ? value[..comment] : value).Trim();
    }

    private static bool IsShellName(string key)
    {
        if (key.Length == 0 || !(char.IsLetter(key[0]) || key[0] == '_'))
        {
            return false;
        }

        return key.All(static c => char.IsLetterOrDigit(c) || c == '_');
    }
}
