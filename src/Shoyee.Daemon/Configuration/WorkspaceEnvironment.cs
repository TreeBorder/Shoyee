namespace Shoyee.Daemon.Configuration;

/// <summary>
/// 工作区 <c>.env</c> 的极简读取：<c>KEY=VALUE</c> 一行一条，<c>#</c> 开头为注释。
///
/// 只补齐**进程环境里还没有**的变量（launchd / 终端里显式设的优先），不回写、不打印值。
/// 这样配置文件只写环境变量名，密钥落在 gitignore 的工作区文件里；
/// 菜单栏 <c>ctl start</c> 与 launchd 两种启动方式都不必各自再注入环境。
/// </summary>
public static class WorkspaceEnvironment
{
    /// <summary>工作区环境变量文件名。</summary>
    public const string FileName = ".env";

    /// <summary>读取 <c>{workspace}/.env</c> 并补齐环境变量。</summary>
    /// <param name="workspaceRoot">工作区根目录。</param>
    /// <returns>本次实际写入的变量名（不含值）。</returns>
    public static IReadOnlyList<string> Apply(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        var path = Path.Combine(workspaceRoot, FileName);
        if (!File.Exists(path))
        {
            return [];
        }

        var applied = new List<string>();
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith("export ", StringComparison.Ordinal))
            {
                line = line["export ".Length..].TrimStart();
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            if (key.Length == 0 || !IsValidKey(key))
            {
                continue;
            }

            var value = Unquote(line[(separator + 1)..].Trim());
            if (Environment.GetEnvironmentVariable(key) is not null)
            {
                continue;
            }

            Environment.SetEnvironmentVariable(key, value);
            applied.Add(key);
        }

        return applied;
    }

    private static bool IsValidKey(string key)
        => key.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

    private static string Unquote(string value)
    {
        if (value.Length >= 2
            && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
        {
            return value[1..^1];
        }

        return value;
    }
}
