using System.Diagnostics;
using Shoyee.Contracts.Abstractions;

namespace Shoyee.Notify.System;

/// <summary>
/// 系统通知：调用常驻 <c>筱.app</c> 的 <c>--notify</c> 入口，由 Swift 壳走
/// <c>UNUserNotificationCenter</c> 发送。通知归属是「筱」，系统设置里只有一个权限条目。
/// </summary>
public sealed class SystemNotificationPusher : INotificationPusher
{
    /// <summary>供应商 key。</summary>
    public const string ProviderKey = "system";

    private readonly SystemNotifyOptions _options;

    /// <summary>创建推送器。</summary>
    /// <param name="options">系统通知自己的配置节。</param>
    public SystemNotificationPusher(SystemNotifyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <inheritdoc />
    public async Task PushAsync(string title, string body, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException("系统通知目前只实现 macOS；Windows 端后续加 Shoyee.Notify.Windows。");
        }

        var executable = ResolveExecutable();
        if (!File.Exists(executable))
        {
            throw new InvalidOperationException(
                $"常驻 筱.app 不存在：{executable}。先构建 daemon（会把 筱.app 产出到输出目录），" +
                "或在 notify.providers.system.executablePath 指定路径。");
        }

        try
        {
            await RunAsync(
                    executable,
                    BuildArguments(title, body, _options.Sound),
                    _options.TimeoutSeconds,
                    "系统通知",
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException ex) when (
            _options.FallbackToScript
            && ex.Message.Contains("Notifications are not allowed", StringComparison.OrdinalIgnoreCase))
        {
            // 自签名/ad-hoc 签名被通知中心拒绝时的临时兜底；归属是「脚本编辑器」。
            await RunAsync(
                    "/usr/bin/osascript",
                    ["-e", BuildAppleScript(title, body)],
                    _options.TimeoutSeconds,
                    "系统通知兜底（osascript）",
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    internal static IReadOnlyList<string> BuildArguments(string title, string body, bool sound)
    {
        var arguments = new List<string> { "--notify", title, body };
        if (!sound)
        {
            arguments.Add("--silent");
        }

        return arguments;
    }

    internal static string BuildAppleScript(string title, string body)
        => $"display notification \"{Escape(body)}\" with title \"{Escape(title)}\"";

    internal static string ResolveExecutablePath(string configured, string baseDirectory)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        // 最终形态：daemon 就在 筱.app/Contents/MacOS/ 里，通知入口是同级 shoyee；
        // 开发形态：daemon 在 bin/.../net10.0/，通知入口在同目录的 筱.app 里。
        var sibling = Path.Combine(baseDirectory, "shoyee");
        return File.Exists(sibling)
            ? sibling
            : Path.Combine(baseDirectory, "筱.app", "Contents", "MacOS", "shoyee");
    }

    private string ResolveExecutable()
        => ResolveExecutablePath(_options.ExecutablePath, AppContext.BaseDirectory);

    private static string Escape(string text)
        => text.ReplaceLineEndings(" ").Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static async Task RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        int timeoutSeconds,
        string operation,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds)));
        try
        {
            var stderr = process.StandardError.ReadToEndAsync(timeoutCts.Token);
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"{operation}失败（exit {process.ExitCode}）：{(await stderr.ConfigureAwait(false)).Trim()}");
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new InvalidOperationException($"{operation}超时（{timeoutSeconds} 秒）。");
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
    }
}
