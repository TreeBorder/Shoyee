using System.Net.Sockets;
using Shoyee.Client.Transport;

namespace Shoyee.Gateway.Transport;

/// <summary>UDS 监听 socket 的建立与残留清理。</summary>
internal static class UdsSocket
{
    /// <summary>
    /// 绑定并开始监听。已有 socket 文件时先试连：连得上说明还有实例，直接报错；
    /// 连不上是上次崩溃的残留，删掉重建。
    /// </summary>
    public static async Task<Socket> BindAsync(string socketPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(socketPath);

        var directory = Path.GetDirectoryName(socketPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    directory,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        if (File.Exists(socketPath))
        {
            if (await CanConnectAsync(socketPath, cancellationToken).ConfigureAwait(false))
            {
                throw new IOException($"已有 daemon 在监听 {socketPath}");
            }

            File.Delete(socketPath);
        }

        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            socket.Bind(new UnixDomainSocketEndPoint(socketPath));
            ShoyeePaths.RestrictFile(socketPath);
            socket.Listen(64);
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static async Task<bool> CanConnectAsync(string socketPath, CancellationToken cancellationToken)
    {
        using var probe = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(300));
        try
        {
            await probe.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), timeout.Token).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            return false;
        }
    }
}
