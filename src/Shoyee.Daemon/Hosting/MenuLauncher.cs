using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Shoyee.Daemon.Hosting;

/// <summary>
/// 从 App bundle 里拉起 Swift 菜单栏。
///
/// 登录项只剩 daemon 一条（<c>com.shoyee.daemon</c>），菜单栏不再单独注册 LaunchAgent：
/// daemon 启动时把 <c>Contents/MacOS/shoyee</c> 当子进程拉起，用户主动退出（退出码 0）后不再重试，
/// 异常退出则隔几秒重启。菜单栏退出不影响 daemon 的 24h 在线。
/// 不在 bundle 里运行（开发时 <c>dotnet run</c>）找不到菜单栏可执行文件，这里直接空转。
/// </summary>
public sealed class MenuLauncher : BackgroundService
{
    private static readonly TimeSpan RestartDelay = TimeSpan.FromSeconds(3);

    private readonly ILogger<MenuLauncher> _logger;

    /// <summary>创建菜单栏拉起服务。</summary>
    /// <param name="logger">日志。</param>
    public MenuLauncher(ILogger<MenuLauncher> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var path = MenuPath();
        if (path is null || !File.Exists(path))
        {
            return;
        }

        if (IsMenuRunning())
        {
            _logger.LogInformation("菜单栏已在运行，跳过拉起");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = false });
                if (process is null)
                {
                    _logger.LogWarning("菜单栏没有启动起来：{Path}", path);
                    return;
                }

                _logger.LogInformation("已拉起菜单栏（pid {Pid}）：{Path}", process.Id, path);
                await process.WaitForExitAsync(stoppingToken).ConfigureAwait(false);

                if (process.ExitCode == 0)
                {
                    _logger.LogInformation("菜单栏已正常退出，不再拉起");
                    return;
                }

                _logger.LogWarning("菜单栏异常退出（{Code}），{Delay} 后重试", process.ExitCode, RestartDelay);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "拉起菜单栏失败");
            }

            try
            {
                await Task.Delay(RestartDelay, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>菜单栏可执行文件：<c>Contents/Resources/daemon/../../MacOS/shoyee</c>；不在 bundle 里返回 null。</summary>
    private static string? MenuPath()
    {
        var baseDir = AppContext.BaseDirectory;
        var path = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "MacOS", "shoyee"));
        return File.Exists(path) ? path : null;
    }

    /// <summary>防止重复拉起：菜单栏进程名是 <c>shoyee</c>（daemon 是 <c>shoyee-daemon</c>）。</summary>
    private static bool IsMenuRunning()
    {
        try
        {
            return Process.GetProcessesByName("shoyee").Length > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
