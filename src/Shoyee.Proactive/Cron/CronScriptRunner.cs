using System.Diagnostics;
using Shoyee.Contracts.Options;

namespace Shoyee.Proactive.Cron;

/// <summary>cron 脚本任务的执行结果。</summary>
/// <param name="ExitCode">退出码；-1 = 超时被回收。</param>
/// <param name="Stdout">标准输出（就是任务产出）。</param>
/// <param name="Stderr">标准错误（失败时进执行台账）。</param>
public sealed record CronScriptResult(int ExitCode, string Stdout, string Stderr)
{
    /// <summary>是否成功。</summary>
    public bool Success => ExitCode == 0;
}

/// <summary>cron 脚本任务的执行面（零 LLM、跑一次命令）。</summary>
public interface ICronScriptRunner
{
    /// <summary>执行命令；超时按 <c>proactive.cronJobs.scriptTimeoutSeconds</c>。</summary>
    /// <param name="command">bash 命令或脚本路径。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<CronScriptResult> RunAsync(string command, CancellationToken cancellationToken = default);
}

/// <summary>
/// <see cref="ICronScriptRunner"/> 的本地实现：<c>/bin/bash -lc {command}</c>，工作目录 = 工作区
/// 的 <c>Workspace/</c>（与终端工具一致）；超时杀进程树。
/// </summary>
public sealed class CronScriptRunner : ICronScriptRunner
{
    private readonly WorkspaceConfig _config;

    /// <summary>创建执行器。</summary>
    /// <param name="config">工作区配置。</param>
    public CronScriptRunner(WorkspaceConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _config = config;
    }

    /// <inheritdoc />
    public async Task<CronScriptResult> RunAsync(string command, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return new CronScriptResult(-1, string.Empty, "脚本任务缺少命令。");
        }

        var shell = _config.Shell;
        var workingDirectory = string.IsNullOrWhiteSpace(shell?.WorkingDirectory)
            ? Path.Combine(_config.Root, "Workspace")
            : Path.GetFullPath(shell.WorkingDirectory);
        Directory.CreateDirectory(workingDirectory);

        var timeout = TimeSpan.FromSeconds(Math.Max(1, _config.Proactive.EffectiveCron.ScriptTimeoutSeconds));
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                ArgumentList = { "-lc", command },
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };

        process.Start();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeoutCts.Token);
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            return new CronScriptResult(
                process.ExitCode,
                Truncate(await stdout.ConfigureAwait(false), _config.Shell?.MaxOutputBytes ?? 200_000),
                await stderr.ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            return new CronScriptResult(-1, string.Empty, $"脚本执行超时（{timeout.TotalSeconds:0} 秒）");
        }
    }

    private static string Truncate(string text, int maxBytes)
    {
        if (text.Length <= maxBytes)
        {
            return text;
        }

        return text[..maxBytes] + "\n…（输出已截断）";
    }
}
