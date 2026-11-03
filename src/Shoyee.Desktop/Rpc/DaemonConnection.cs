using System.Net.Sockets;
using System.Text.Json;
using Shoyee.Client.Rpc;
using Shoyee.Client.Transport;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Options;
using Shoyee.Contracts.Rpc;

namespace Shoyee.Desktop.Rpc;

/// <summary>daemon 不可用（没起 / 连接已断）时抛出的错误；界面按「未连接」文案处理。</summary>
public sealed class DaemonUnavailableException : Exception
{
    /// <summary>创建异常。</summary>
    /// <param name="message">说明。</param>
    /// <param name="inner">底层异常。</param>
    public DaemonUnavailableException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

/// <summary>
/// 桌面侧与 daemon 的连接：一条 <c>desktop-work</c> + 一条 <c>desktop-roleplay</c> 的
/// UDS JSON-RPC 连接，后台负责 hello、20s ping 与断线重连。
///
/// 两条连接分开是契约要求：服务端按 role 过滤事件与方法，工作台不该收到 RP Lane 的事件。
/// RP 连接失败不阻断工作台（本轮 RP 页面仍用导入数据）。
/// </summary>
public sealed class DaemonConnection : IAsyncDisposable
{
    private readonly ShoyeePaths _paths;
    private readonly TimeSpan _initialRetryDelay;
    private readonly TimeSpan _maxRetryDelay;
    private readonly CancellationTokenSource _cts = new();
    private RpcClient? _work;
    private RpcClient? _roleplay;
    private Task? _loop;
    private DaemonConnectionState _state = new(DaemonStatus.Disconnected);
    private int _disposed;

    /// <summary>创建连接。</summary>
    /// <param name="paths">socket 路径来源。</param>
    /// <param name="initialRetryDelay">断线后首次重试间隔（默认 2s）。</param>
    /// <param name="maxRetryDelay">重试间隔上限（默认 15s）。</param>
    public DaemonConnection(
        ShoyeePaths paths,
        TimeSpan? initialRetryDelay = null,
        TimeSpan? maxRetryDelay = null)
    {
        _paths = paths;
        _initialRetryDelay = initialRetryDelay ?? TimeSpan.FromSeconds(2);
        _maxRetryDelay = maxRetryDelay ?? TimeSpan.FromSeconds(15);
    }

    /// <summary>连接状态变化（可能从读取线程触发，订阅方自己切回 UI 线程）。只在状态真正变化时发。</summary>
    public event Action<DaemonConnectionState>? StateChanged;

    /// <summary>工作连接收到的事件（事件名，载荷）；在读取线程上同步触发。</summary>
    public event Action<string, JsonElement>? WorkEventReceived;

    /// <summary>角色模式连接收到的事件；在读取线程上同步触发。</summary>
    public event Action<string, JsonElement>? RoleplayEventReceived;

    /// <summary>当前状态。</summary>
    public DaemonConnectionState State => Volatile.Read(ref _state);

    /// <summary>工作连接是否可用。</summary>
    public bool IsConnected => _work is { IsConnected: true };

    /// <summary>角色模式连接是否可用（不可用时 RP 页退回只读/未连接提示）。</summary>
    public bool IsRoleplayConnected => _roleplay is { IsConnected: true };

    /// <summary>socket 路径（状态文案用）。</summary>
    public string SocketPath => _paths.SocketPath;

    /// <summary>启动后台连接循环（幂等）。</summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        _loop ??= Task.Run(() => RunAsync(_cts.Token));
    }

    /// <summary>发一个工作连接请求并按类型反序列化结果。</summary>
    /// <typeparam name="T">结果类型。</typeparam>
    /// <param name="method">方法名（<see cref="RpcMethods"/>）。</param>
    /// <param name="parameters">参数对象。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<T> CallWorkAsync<T>(
        string method,
        object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var client = _work;
        if (client is null || !client.IsConnected)
        {
            throw new DaemonUnavailableException("daemon 未连接");
        }

        try
        {
            return await client.CallAsync<T>(method, parameters, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsConnectionLoss(ex, cancellationToken))
        {
            throw new DaemonUnavailableException("与 daemon 的连接已断开", ex);
        }
    }

    /// <summary>发一个角色模式连接请求并按类型反序列化结果。</summary>
    /// <typeparam name="T">结果类型。</typeparam>
    /// <param name="method">方法名（<see cref="RpcMethods"/>）。</param>
    /// <param name="parameters">参数对象。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<T> CallRoleplayAsync<T>(
        string method,
        object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var client = _roleplay;
        if (client is null || !client.IsConnected)
        {
            throw new DaemonUnavailableException("角色模式未连接 daemon");
        }

        try
        {
            return await client.CallAsync<T>(method, parameters, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsConnectionLoss(ex, cancellationToken))
        {
            throw new DaemonUnavailableException("与 daemon 的角色模式连接已断开", ex);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _cts.CancelAsync().ConfigureAwait(false);
        if (_loop is not null)
        {
            try
            {
                await _loop.ConfigureAwait(false);
            }
            catch
            {
                // 关闭阶段的异常无关紧要
            }
        }

        await DisposeClientsAsync().ConfigureAwait(false);
        _cts.Dispose();
    }

    private static bool IsConnectionLoss(Exception ex, CancellationToken cancellationToken)
        => ex is IOException or SocketException or ObjectDisposedException
           || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested);

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var delay = _initialRetryDelay;
        while (!cancellationToken.IsCancellationRequested)
        {
            var roleplayError = await TryConnectAsync(cancellationToken).ConfigureAwait(false);
            if (IsConnected)
            {
                delay = _initialRetryDelay;
                try
                {
                    await KeepAliveAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    SetState(new DaemonConnectionState(DaemonStatus.Disconnected, Error: Describe(ex)));
                }
            }
            else if (roleplayError is not null)
            {
                // 工作连接没起来时错误已经在 SetState 里；这里只是保证不会静默空转
                SetState(new DaemonConnectionState(DaemonStatus.Disconnected, Error: roleplayError));
            }

            await DisposeClientsAsync().ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (!IsConnected && State.Status != DaemonStatus.Connecting)
            {
                SetState(new DaemonConnectionState(DaemonStatus.Disconnected, Error: State.Error));
            }

            try
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            var next = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 1.5, _maxRetryDelay.TotalSeconds));
            delay = next;
        }

        await DisposeClientsAsync().ConfigureAwait(false);
        SetState(new DaemonConnectionState(DaemonStatus.Disconnected));
    }

    /// <summary>连两条连接；返回 RP 连接的错误（工作连接失败会直接体现在状态里）。</summary>
    private async Task<string?> TryConnectAsync(CancellationToken cancellationToken)
    {
        if (State.Status != DaemonStatus.Connecting)
        {
            SetState(new DaemonConnectionState(DaemonStatus.Connecting));
        }

        RpcClient work;
        try
        {
            work = await ConnectAsync(ClientRole.DesktopWork, laneId: null, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            SetState(new DaemonConnectionState(DaemonStatus.Disconnected, Error: Describe(ex)));
            return null;
        }

        work.EventReceived += OnWorkEvent;
        Volatile.Write(ref _work, work);

        var roleplayError = await TryConnectRoleplayAsync(cancellationToken).ConfigureAwait(false);
        SetState(new DaemonConnectionState(DaemonStatus.Connected, work.Hello.Workspace, roleplayError));
        return roleplayError;
    }

    private async Task<string?> TryConnectRoleplayAsync(CancellationToken cancellationToken)
    {
        if (_roleplay is { IsConnected: true })
        {
            return null;
        }

        try
        {
            var roleplay = await ConnectAsync(
                    ClientRole.DesktopRoleplay,
                    LaneIds.DesktopRoleplay,
                    cancellationToken)
                .ConfigureAwait(false);
            roleplay.EventReceived += OnRoleplayEvent;
            Volatile.Write(ref _roleplay, roleplay);
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // RP 本轮不用；连不上不影响工作台
            return Describe(ex);
        }
    }

    private Task<RpcClient> ConnectAsync(ClientRole role, string? laneId, CancellationToken cancellationToken)
        => RpcClient.ConnectAsync(
            _paths.SocketPath,
            new HelloRequest("desktop", RpcProtocol.Version, role, laneId),
            cancellationToken: cancellationToken);

    /// <summary>ping 循环；任一连接失效就退出，交给外层重连。</summary>
    private async Task KeepAliveAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(RpcProtocol.PingInterval, cancellationToken).ConfigureAwait(false);

            var work = _work;
            if (work is null || !work.IsConnected)
            {
                return;
            }

            await work.PingAsync(cancellationToken).ConfigureAwait(false);

            var roleplay = _roleplay;
            if (roleplay is null || !roleplay.IsConnected)
            {
                // 顺带把 RP 连接补上；失败不打断工作台
                if (roleplay is not null)
                {
                    roleplay.EventReceived -= OnRoleplayEvent;
                    await roleplay.DisposeAsync().ConfigureAwait(false);
                    Volatile.Write(ref _roleplay, null);
                }

                await TryConnectRoleplayAsync(cancellationToken).ConfigureAwait(false);
                if (State is { Status: DaemonStatus.Connected, Error: not null })
                {
                    // 角色通道补连成功：清掉状态里的错误，顺带通知界面重新拉一次 RP 时间线。
                    SetState(State with { Error = null });
                }

                continue;
            }

            try
            {
                await roleplay.PingAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // RP 掉线只丢 RP 连接，工作台继续
                roleplay.EventReceived -= OnRoleplayEvent;
                await roleplay.DisposeAsync().ConfigureAwait(false);
                Volatile.Write(ref _roleplay, null);
            }
        }
    }

    private void OnWorkEvent(string name, JsonElement payload) => WorkEventReceived?.Invoke(name, payload);

    private void OnRoleplayEvent(string name, JsonElement payload) => RoleplayEventReceived?.Invoke(name, payload);

    private void SetState(DaemonConnectionState state)
    {
        var previous = Interlocked.Exchange(ref _state, state);
        if (previous == state)
        {
            return;
        }

        StateChanged?.Invoke(state);
    }

    private async Task DisposeClientsAsync()
    {
        var roleplay = Interlocked.Exchange(ref _roleplay, null);
        if (roleplay is not null)
        {
            roleplay.EventReceived -= OnRoleplayEvent;
            try
            {
                await roleplay.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // 已断开的连接再关一次不算错误
            }
        }

        var work = Interlocked.Exchange(ref _work, null);
        if (work is not null)
        {
            work.EventReceived -= OnWorkEvent;
            try
            {
                await work.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // 同上
            }
        }
    }

    private static string Describe(Exception ex) => ex switch
    {
        SocketException => "找不到 daemon（socket 未就绪）",
        RpcClientException rpc => rpc.Message,
        _ => ex.Message,
    };
}
