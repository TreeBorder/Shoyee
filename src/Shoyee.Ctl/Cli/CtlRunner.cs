using System.Text.Json;
using Shoyee.Client.Rpc;
using Shoyee.Client.Transport;
using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Rpc;
using Shoyee.Ctl.Daemon;

namespace Shoyee.Ctl.Cli;

/// <summary>
/// 命令分发。每个命令都是「连上 socket → 发一帧 → 打印」，
/// 不在 ctl 里做业务判断（启停以外没有本地状态）。
/// </summary>
public static class CtlRunner
{
    /// <summary>跑一个命令，返回进程退出码。</summary>
    /// <param name="args">命令行参数（不含可执行文件名）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var parsed = CliArguments.Parse(args);
        var json = parsed.Has("json");
        var paths = ShoyeePaths.Default;

        try
        {
            return parsed.Command switch
            {
                null or "help" or "--help" or "-h" => Help(),
                "status" => await StatusAsync(paths, json, cancellationToken).ConfigureAwait(false),
                "start" => await StartAsync(paths, json, cancellationToken).ConfigureAwait(false),
                "stop" or "shutdown" => await StopAsync(paths, json, cancellationToken).ConfigureAwait(false),
                "restart" => await RestartAsync(paths, json, cancellationToken).ConfigureAwait(false),
                "reload" => await ReloadAsync(paths, json, cancellationToken).ConfigureAwait(false),
                "config" => await ConfigAsync(paths, json, cancellationToken).ConfigureAwait(false),
                "notify" => await NotifyAsync(paths, json, parsed, cancellationToken).ConfigureAwait(false),
                _ => CtlOutput.Fail(
                    $"未知命令：{parsed.Command}（shoyee-ctl help 看用法）",
                    "usage",
                    json,
                    CtlExitCodes.Usage),
            };
        }
        catch (DaemonNotRunningException ex)
        {
            return CtlOutput.Fail(ex.Message, "not_running", json, CtlExitCodes.NotRunning);
        }
        catch (RpcClientException ex)
        {
            return CtlOutput.Fail($"RPC 失败（{ex.Code}）：{ex.Message}", "failed", json, CtlExitCodes.Failed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return CtlOutput.Fail("已取消", "failed", json, CtlExitCodes.Failed);
        }
    }

    private static int Help()
    {
        Console.Out.WriteLine(
            """
            shoyee-ctl — 筱的轻量控制命令（菜单栏与终端共用）

            用法：
              shoyee-ctl status  [--json]                      daemon 与各 Lane 状态
              shoyee-ctl start   [--json]                      启动 daemon 并等 socket 就绪
              shoyee-ctl stop    [--json]                      优雅停止 daemon
              shoyee-ctl restart [--json]                      重启
              shoyee-ctl reload  [--json]                      重载工作区配置（只热更新不碰模型前缀的项）
              shoyee-ctl config  [--json]                      打印当前工作区配置（凭据是 ${ENV}）
              shoyee-ctl notify  [--title T] [--body B] [--json]  发一条测试通知
              shoyee-ctl help

            环境变量：
              SHOYEE_HOME    应用数据根目录（默认 ~/Library/Application Support/Shoyee）
              SHOYEE_DAEMON  daemon 可执行文件路径（start 用；默认找 ctl 同目录/同一 bundle）
            """);
        return CtlExitCodes.Ok;
    }

    private static async Task<int> StatusAsync(
        ShoyeePaths paths,
        bool json,
        CancellationToken cancellationToken)
    {
        await using var client = await CtlConnection.ConnectAsync(paths, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var result = await client.CallAsync(RpcMethods.DaemonStatus, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var status = result.Deserialize<DaemonStatusDto>(JsonWire.Options)
                     ?? throw new RpcClientException(RpcErrors.Internal, "daemon.status 返回为空");
        CtlOutput.Result(result, json, StatusReport.Format(status));
        return CtlExitCodes.Ok;
    }

    private static async Task<int> StartAsync(
        ShoyeePaths paths,
        bool json,
        CancellationToken cancellationToken)
    {
        var result = await DaemonProcess.StartAsync(paths, cancellationToken).ConfigureAwait(false);
        if (result.Error is not null)
        {
            return CtlOutput.Fail(result.Error, "failed", json, CtlExitCodes.Failed);
        }

        CtlOutput.Success(
            result.AlreadyRunning ? "daemon 已经在运行。" : "daemon 已启动。",
            json);
        return CtlExitCodes.Ok;
    }

    private static async Task<int> StopAsync(
        ShoyeePaths paths,
        bool json,
        CancellationToken cancellationToken)
    {
        var stopped = await DaemonProcess.StopAsync(paths, cancellationToken).ConfigureAwait(false);
        if (!stopped)
        {
            return CtlOutput.Fail(
                $"daemon 未运行（socket：{paths.SocketPath}）",
                "not_running",
                json,
                CtlExitCodes.NotRunning);
        }

        CtlOutput.Success("daemon 已停止。", json);
        return CtlExitCodes.Ok;
    }

    private static async Task<int> RestartAsync(
        ShoyeePaths paths,
        bool json,
        CancellationToken cancellationToken)
    {
        var wasRunning = await DaemonProcess.StopAsync(paths, cancellationToken).ConfigureAwait(false);
        var result = await DaemonProcess.StartAsync(paths, cancellationToken).ConfigureAwait(false);
        if (result.Error is not null)
        {
            return CtlOutput.Fail(result.Error, "failed", json, CtlExitCodes.Failed);
        }

        CtlOutput.Success(wasRunning ? "daemon 已重启。" : "daemon 已启动（原先没在跑）。", json);
        return CtlExitCodes.Ok;
    }

    private static async Task<int> ReloadAsync(
        ShoyeePaths paths,
        bool json,
        CancellationToken cancellationToken)
    {
        await using var client = await CtlConnection.ConnectAsync(paths, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var result = await client.CallAsync(RpcMethods.ConfigReload, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var reload = result.Deserialize<ConfigReloadResult>(JsonWire.Options) ?? new ConfigReloadResult([], []);
        CtlOutput.Result(result, json, FormatReload(reload));
        return CtlExitCodes.Ok;
    }

    private static async Task<int> ConfigAsync(
        ShoyeePaths paths,
        bool json,
        CancellationToken cancellationToken)
    {
        await using var client = await CtlConnection.ConnectAsync(paths, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var result = await client.CallAsync(RpcMethods.ConfigGet, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        // 配置本身就是 JSON：两种模式都原样打印，别造一份「可读版」再和真源分叉
        Console.Out.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonWire.Options)
        {
            WriteIndented = true,
        }));
        return CtlExitCodes.Ok;
    }

    private static async Task<int> NotifyAsync(
        ShoyeePaths paths,
        bool json,
        CliArguments parsed,
        CancellationToken cancellationToken)
    {
        await using var client = await CtlConnection.ConnectAsync(paths, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var result = await client.CallAsync(
                RpcMethods.NotifyTest,
                new NotifyTestRequest(parsed.Get("title"), parsed.Get("body")),
                cancellationToken)
            .ConfigureAwait(false);
        var notify = result.Deserialize<NotifyTestResult>(JsonWire.Options)
                     ?? throw new RpcClientException(RpcErrors.Internal, "notify.test 返回为空");
        if (!notify.Success)
        {
            return CtlOutput.Fail(
                $"通知发送失败（{notify.Provider}）：{notify.Error}",
                "failed",
                json,
                CtlExitCodes.Failed);
        }

        CtlOutput.Result(result, json, $"已通过 {notify.Provider} 发送测试通知。");
        return CtlExitCodes.Ok;
    }

    private static string FormatReload(ConfigReloadResult result)
    {
        if (result.Changed.Count == 0 && result.RestartRequired.Count == 0)
        {
            return "配置没有变化。";
        }

        var lines = new List<string>();
        if (result.Changed.Count > 0)
        {
            lines.Add("已热更新：" + string.Join("、", result.Changed));
        }

        if (result.RestartRequired.Count > 0)
        {
            lines.Add("需重启 daemon 才生效：" + string.Join("、", result.RestartRequired));
        }

        return string.Join(Environment.NewLine, lines);
    }
}
