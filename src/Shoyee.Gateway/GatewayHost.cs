using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shoyee.Client.Transport;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Rpc;
using Shoyee.Gateway.Rpc;
using Shoyee.Gateway.Subscriptions;
using Shoyee.Gateway.Transport;

namespace Shoyee.Gateway;

/// <summary>
/// daemon 的传输宿主：起 UDS 监听、接收连接、停机时先停止接新连接 →
/// 等在跑/排队轮次最多 10 秒 → 仍有则取消 → 关闭连接 → 删 socket。
/// </summary>
public sealed class GatewayHost : IHostedService, IAsyncDisposable
{
    private readonly RpcServices _services;
    private readonly EventHub _hub;
    private readonly RpcDispatcher _dispatcher;
    private readonly ShutdownGate _gate;
    private readonly GatewayOptions _options;
    private readonly ILogger<GatewayHost> _logger;
    private CancellationTokenSource? _cts;
    private Socket? _listener;
    private Task? _acceptLoop;
    private Task? _statusLoop;
    private int _disposed;

    /// <summary>创建宿主。</summary>
    /// <param name="services">方法依赖（含 Lane 仓库与停机收尾面）。</param>
    /// <param name="hub">事件总线。</param>
    /// <param name="dispatcher">RPC 分发器。</param>
    /// <param name="gate">停机闸门。</param>
    /// <param name="options">进程级参数。</param>
    /// <param name="logger">日志。</param>
    public GatewayHost(
        RpcServices services,
        EventHub hub,
        RpcDispatcher dispatcher,
        ShutdownGate gate,
        GatewayOptions options,
        ILogger<GatewayHost>? logger = null)
    {
        _services = services;
        _hub = hub;
        _dispatcher = dispatcher;
        _gate = gate;
        _options = options;
        _logger = logger ?? NullLogger<GatewayHost>.Instance;
    }

    /// <summary>是否已开始监听。</summary>
    public bool IsListening => _listener is not null;

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = new CancellationTokenSource();
        _listener = await UdsSocket.BindAsync(_options.SocketPath, cancellationToken).ConfigureAwait(false);

        var roleplayLanes = await _services.Lanes
            .ListAsync(Mode.Roleplay, cancellationToken)
            .ConfigureAwait(false);
        _hub.SetRoleplayLanes(roleplayLanes.Select(static lane => lane.Id));

        _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token), CancellationToken.None);
        _statusLoop = Task.Run(() => StatusLoopAsync(_cts.Token), CancellationToken.None);
        _logger.LogInformation("Gateway 已监听 {Socket}（工作区 {Workspace}）", _options.SocketPath, _options.Workspace);
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _gate.MarkStopping();
        CloseListener();

        if (_services.Drain is { } drain)
        {
            var idle = await drain.WaitForIdleAsync(_options.ShutdownTurnGrace, CancellationToken.None)
                .ConfigureAwait(false);
            if (!idle)
            {
                _logger.LogWarning(
                    "仍有 {Count} 个轮次未收束，超过 {Grace} 秒，按取消处理",
                    drain.ActiveTurnCount,
                    _options.ShutdownTurnGrace);
                drain.CancelAll();
                await drain.WaitForIdleAsync(_options.ShutdownCancelGrace, CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }

        await _hub.CloseAllAsync().ConfigureAwait(false);

        if (_cts is not null)
        {
            await _cts.CancelAsync().ConfigureAwait(false);
        }

        foreach (var loop in new[] { _acceptLoop, _statusLoop })
        {
            if (loop is null)
            {
                continue;
            }

            try
            {
                await loop.ConfigureAwait(false);
            }
            catch
            {
                // 停机时循环退出异常不是停机失败
            }
        }

        DeleteSocketFile();
        _logger.LogInformation("Gateway 已停止");
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        CloseListener();
        DeleteSocketFile();
        await _hub.CloseAllAsync().ConfigureAwait(false);
        if (_cts is not null)
        {
            _cts.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        var listener = _listener;
        if (listener is null)
        {
            return;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            Socket socket;
            try
            {
                socket = await listener.AcceptAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException ex)
            {
                _logger.LogWarning(ex, "接受连接失败，监听继续");
                continue;
            }

            var connection = new RpcServerConnection(
                socket,
                _dispatcher,
                _hub,
                cancellationToken,
                _logger);
            _ = connection.RunAsync();
        }
    }

    private async Task StatusLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_options.StatusPushInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                if (_hub.ConnectionCount == 0)
                {
                    continue;
                }

                var lanes = await RpcHandlers
                    .BuildLaneStatusesAsync(_services.Lanes, cancellationToken, _services.Channels)
                    .ConfigureAwait(false);
                var payload = new DaemonStatusDto(
                    _options.Version,
                    (long)Math.Max(0, (DateTimeOffset.UtcNow - _options.StartedAt).TotalSeconds),
                    _options.SocketPath,
                    lanes);
                await _hub.PublishAsync(RpcEvents.DaemonStatus, payload, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "daemon.status 心跳推送退出");
        }
    }

    private void CloseListener()
    {
        var listener = Interlocked.Exchange(ref _listener, null);
        listener?.Dispose();
    }

    private void DeleteSocketFile()
    {
        try
        {
            if (File.Exists(_options.SocketPath))
            {
                File.Delete(_options.SocketPath);
            }
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "删除 socket 文件失败：{Socket}", _options.SocketPath);
        }
    }
}
