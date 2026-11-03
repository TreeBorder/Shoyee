namespace Shoyee.Client.Transport;

/// <summary>
/// daemon 的固定路径（socket / 锁 / 日志 / 配置）。
///
/// 默认根目录是 <c>~/Library/Application Support/Shoyee</c>；
/// 环境变量 <c>SHOYEE_HOME</c> 可覆盖（测试与并行实例用）。
/// socket 不随工作区走——工作区可能在移动硬盘上。
/// </summary>
public sealed class ShoyeePaths
{
    /// <summary>覆盖根目录的环境变量名。</summary>
    public const string HomeEnvVar = "SHOYEE_HOME";

    /// <summary>创建路径集合。</summary>
    /// <param name="appDataRoot">应用数据根目录（绝对路径）。</param>
    public ShoyeePaths(string appDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDataRoot);
        AppDataRoot = Path.GetFullPath(appDataRoot);
    }

    /// <summary>按环境变量或系统默认位置创建。</summary>
    public static ShoyeePaths Default => new(ResolveDefaultRoot());

    /// <summary>应用数据根目录。</summary>
    public string AppDataRoot { get; }

    /// <summary>daemon 引导配置（只含 <c>workspace</c> 等进程级参数）。</summary>
    public string ConfigPath => Path.Combine(AppDataRoot, "config.json");

    /// <summary>运行目录（socket / 锁都在这里）。</summary>
    public string RunDirectory => Path.Combine(AppDataRoot, "run");

    /// <summary>Unix domain socket。</summary>
    public string SocketPath => Path.Combine(RunDirectory, "shoyee.sock");

    /// <summary>单实例锁文件。</summary>
    public string LockPath => Path.Combine(RunDirectory, "shoyee.lock");

    /// <summary>日志目录。</summary>
    public string LogDirectory => Path.Combine(AppDataRoot, "logs");

    /// <summary>daemon 日志文件。</summary>
    public string LogPath => Path.Combine(LogDirectory, "daemon.log");

    /// <summary>创建运行目录并收到 0700。</summary>
    public void EnsureRunDirectory()
    {
        Directory.CreateDirectory(RunDirectory);
        TrySetMode(RunDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    /// <summary>创建日志目录（0700）。</summary>
    public void EnsureLogDirectory()
    {
        Directory.CreateDirectory(LogDirectory);
        TrySetMode(LogDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    /// <summary>给 socket / 锁文件收到 0600；非 Unix 平台静默跳过。</summary>
    /// <param name="path">文件路径。</param>
    public static void RestrictFile(string path)
        => TrySetMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);

    private static void TrySetMode(string path, UnixFileMode mode)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            // File.SetUnixFileMode 对目录同样适用（走 chmod）
            File.SetUnixFileMode(path, mode);
        }
        catch (IOException)
        {
            // 权限收紧失败不该拦启动（本地即身份，最坏是同机其他用户可读）
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string ResolveDefaultRoot()
    {
        var custom = Environment.GetEnvironmentVariable(HomeEnvVar);
        if (!string.IsNullOrWhiteSpace(custom))
        {
            return custom;
        }

        // macOS 固定 ~/Library/Application Support；.NET 的 SpecialFolder 在 Unix 上不映射这个目录
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, "Library", "Application Support", "Shoyee");
    }
}
