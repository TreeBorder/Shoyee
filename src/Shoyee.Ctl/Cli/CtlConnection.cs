using System.Net.Sockets;
using Shoyee.Client.Rpc;
using Shoyee.Client.Transport;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Rpc;

namespace Shoyee.Ctl.Cli;

/// <summary>daemon 没在跑（socket 连不上）。调用方据此给退出码 2。</summary>
public sealed class DaemonNotRunningException : Exception
{
    /// <summary>创建异常。</summary>
    /// <param name="socketPath">尝试连接的 socket。</param>
    /// <param name="inner">底层原因。</param>
    public DaemonNotRunningException(string socketPath, Exception? inner = null)
        : base($"daemon 未运行（socket：{socketPath}）", inner)
    {
        SocketPath = socketPath;
    }

    /// <summary>尝试连接的 socket 路径。</summary>
    public string SocketPath { get; }
}

/// <summary>ctl 的连接入口：固定 <see cref="ClientRole.Ctl"/> 角色与短超时。</summary>
public static class CtlConnection
{
    /// <summary>连接并握手；连不上折成 <see cref="DaemonNotRunningException"/>。</summary>
    /// <param name="paths">路径集合。</param>
    /// <param name="timeout">连接超时（null = 3s）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public static async Task<RpcClient> ConnectAsync(
        ShoyeePaths paths,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        try
        {
            return await RpcClient.ConnectAsync(
                    paths.SocketPath,
                    new HelloRequest("ctl", RpcProtocol.Version, ClientRole.Ctl),
                    timeout ?? TimeSpan.FromSeconds(3),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // 连接超时按「没在跑」处理：菜单栏只关心按钮能不能点
            throw new DaemonNotRunningException(paths.SocketPath);
        }
        catch (Exception ex) when (ex is SocketException or IOException)
        {
            throw new DaemonNotRunningException(paths.SocketPath, ex);
        }
    }
}
