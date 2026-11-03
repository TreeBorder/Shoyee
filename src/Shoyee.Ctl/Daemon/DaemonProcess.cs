using System.Diagnostics;
using Shoyee.Client.Rpc;
using Shoyee.Client.Transport;
using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Rpc;
using Shoyee.Ctl.Cli;

namespace Shoyee.Ctl.Daemon;

/// <summary>一次启动尝试的结果。</summary>
/// <param name="Started">本次是否真的启动了新进程（已经在跑 = false 且 <paramref name="AlreadyRunning"/> 为 true）。</param>
/// <param name="AlreadyRunning">启动前就已经连得上。</param>
/// <param name="Status">启动后拿到的状态（超时时 null）。</param>
/// <param name="Error">失败原因。</param>
public sealed record DaemonStartResult(
    bool Started,
    bool AlreadyRunning,
    DaemonStatusDto? Status,
    string? Error);

/// <summary>
/// 菜单栏的「启动 / 停止」：ctl 只负责拉起可执行文件、等 socket 出现；
/// 常驻/开机自启仍归 launchd（部署阶段再接）。
/// </summary>
public static class DaemonProcess
{
    /// <summary>覆盖 daemon 可执行文件路径的环境变量（开发时用）。</summary>
    public const string PathEnvVar = "SHOYEE_DAEMON";

    /// <summary>launchd 的 job 标签（<c>tools/install-launchd.sh</c> 装的那一份）。</summary>
    public const string LaunchdLabel = "com.shoyee.daemon";

    /// <summary>等 socket 出现的最长时间。</summary>
    public static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(20);

    /// <summary>找 daemon 可执行文件；找不到返回 null。</summary>
    /// <remarks>
    /// 顺序：<c>SHOYEE_DAEMON</c> → ctl 同目录（bundle 的 Contents/MacOS）→
    /// bundle 的 Contents/Resources/daemon（install-app.sh 的发布布局）→ PATH。
    /// </remarks>
    public static string? Locate()
    {
        foreach (var candidate in Candidates())
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>启动 daemon 并等它可连。</summary>
    /// <param name="paths">路径集合（socket/日志）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public static async Task<DaemonStartResult> StartAsync(
        ShoyeePaths paths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);

        if (await TryStatusAsync(paths, cancellationToken).ConfigureAwait(false) is { } running)
        {
            return new DaemonStartResult(Started: false, AlreadyRunning: true, Status: running, Error: null);
        }

        var executable = Locate();
        if (executable is null)
        {
            return new DaemonStartResult(
                Started: false,
                AlreadyRunning: false,
                Status: null,
                Error: $"找不到 daemon 可执行文件。设置 {PathEnvVar} 指向 Shoyee.Daemon，"
                       + "或用 install-app.sh 安装完整的 筱.app。");
        }

        paths.EnsureLogDirectory();
        Process? process = null;
        var usedLaunchd = false;
        try
        {
            if (IsLaunchdLoaded())
            {
                // launchd 装好了就让 launchd 拉起，沿用同一份 KeepAlive / 日志配置
                usedLaunchd = true;
                process = RunShell($"launchctl kickstart \"gui/$(id -u)/{LaunchdLabel}\"");
            }
            else
            {
                // 让 daemon 的输出直接进日志：ctl 退出后管道会断，不能靠重定向到自己的 stdout
                process = RunShell("exec \"$1\" >> \"$2\" 2>&1", executable, paths.LogPath);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new DaemonStartResult(false, false, null, "启动 daemon 失败：" + ex.Message);
        }

        var deadline = DateTimeOffset.UtcNow + StartupTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            if (await TryStatusAsync(paths, cancellationToken).ConfigureAwait(false) is { } status)
            {
                return new DaemonStartResult(Started: true, AlreadyRunning: false, status, Error: null);
            }

            // 进程已经退了就别等到超时：配置错/缺密钥这类失败在日志第一屏就有原因
            // （launchd 的 kickstart 本身会立即退出，不能当失败）
            if (!usedLaunchd && process is { HasExited: true })
            {
                return new DaemonStartResult(
                    Started: false,
                    AlreadyRunning: false,
                    Status: null,
                    Error: $"daemon 启动后立即退出（退出码 {process.ExitCode}）；日志：{paths.LogPath}");
            }
        }

        return new DaemonStartResult(
            Started: true,
            AlreadyRunning: false,
            Status: null,
            Error: $"daemon 已拉起，但 {StartupTimeout.TotalSeconds:F0} 秒内没有就绪；日志：{paths.LogPath}");
    }

    /// <summary>优雅停止：发 <c>daemon.shutdown</c>，再等 socket 消失。</summary>
    /// <param name="paths">路径集合。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>停止前是否在跑（false = 本来就没跑）。</returns>
    public static async Task<bool> StopAsync(
        ShoyeePaths paths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);

        try
        {
            await using var client = await CtlConnection.ConnectAsync(paths, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            await client.CallAsync(RpcMethods.DaemonShutdown, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (DaemonNotRunningException)
        {
            return false;
        }

        var deadline = DateTimeOffset.UtcNow + StartupTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(paths.SocketPath))
            {
                return true;
            }

            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    private static async Task<DaemonStatusDto?> TryStatusAsync(
        ShoyeePaths paths,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var client = await CtlConnection
                .ConnectAsync(paths, TimeSpan.FromSeconds(2), cancellationToken)
                .ConfigureAwait(false);
            return await client.CallAsync<DaemonStatusDto>(RpcMethods.DaemonStatus, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (DaemonNotRunningException)
        {
            return null;
        }
        catch (RpcClientException)
        {
            // 版本不匹配/角色拒绝等：能连上就算在跑，状态留给后续调用报具体错
            return new DaemonStatusDto(string.Empty, 0, paths.SocketPath, []);
        }
    }

    /// <summary>launchd 里是否已经装了 daemon 的 job（装了就让 launchd 负责拉起）。</summary>
    private static bool IsLaunchdLoaded()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return false;
        }

        try
        {
            using var probe = RunShell($"launchctl list {LaunchdLabel}");
            probe?.WaitForExit();
            return probe is { ExitCode: 0 };
        }
        catch
        {
            return false;
        }
    }

    /// <summary>起一个非交互 shell；输出不接管道（daemon/launchctl 自己处理）。</summary>
    private static Process? RunShell(string command, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("/bin/sh") { UseShellExecute = false };
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(command);
        if (arguments.Length > 0)
        {
            startInfo.ArgumentList.Add("sh");
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }
        }

        return Process.Start(startInfo);
    }

    private static IEnumerable<string> Candidates()
    {
        var fromEnv = Environment.GetEnvironmentVariable(PathEnvVar);
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            yield return fromEnv;
        }

        var baseDirectory = AppContext.BaseDirectory;
        yield return Path.Combine(baseDirectory, "shoyee-daemon");
        yield return Path.Combine(baseDirectory, "Shoyee.Daemon");
        yield return Path.Combine(baseDirectory, "..", "Resources", "daemon", "Shoyee.Daemon");
        yield return Path.Combine(baseDirectory, "..", "Resources", "daemon", "shoyee-daemon");
    }
}
