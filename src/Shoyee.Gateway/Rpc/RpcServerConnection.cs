using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shoyee.Client.Transport;
using Shoyee.Contracts.Rpc;
using Shoyee.Gateway.Subscriptions;

namespace Shoyee.Gateway.Rpc;

/// <summary>
/// 一条客户端连接：帧读取（带 hello / 空闲超时）、请求分发、事件写出。
/// 同一连接上的写入串行化；请求处理可并发（响应按 id 配对，不依赖顺序）。
/// </summary>
internal sealed class RpcServerConnection : IAsyncDisposable
{
    private readonly Socket _socket;
    private readonly NetworkStream _stream;
    private readonly RpcDispatcher _dispatcher;
    private readonly EventHub _hub;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _cts;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private int _disposed;

    public RpcServerConnection(
        Socket socket,
        RpcDispatcher dispatcher,
        EventHub hub,
        CancellationToken serverToken,
        ILogger? logger = null)
    {
        _socket = socket;
        _stream = new NetworkStream(socket, ownsSocket: false);
        _dispatcher = dispatcher;
        _hub = hub;
        _logger = logger ?? NullLogger.Instance;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(serverToken);
    }

    public Guid Id { get; } = Guid.NewGuid();

    public RpcConnectionState State { get; } = new();

    public async Task RunAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var frame = await ReadFrameAsync().ConfigureAwait(false);
                if (frame is null)
                {
                    break;
                }

                RpcRequest? request;
                try
                {
                    request = JsonSerializer.Deserialize<RpcRequest>(frame, JsonWire.Options);
                }
                catch (JsonException ex)
                {
                    await WriteErrorAsync(null, RpcErrors.InvalidParams, "请求不是合法 JSON-RPC", ex.Message)
                        .ConfigureAwait(false);
                    break;
                }

                if (request is null || string.IsNullOrWhiteSpace(request.Method))
                {
                    await WriteErrorAsync(request?.Id, RpcErrors.InvalidParams, "缺少 method").ConfigureAwait(false);
                    break;
                }

                // ping 是 notification：不回包，只刷新空闲计时
                if (string.Equals(request.Method, RpcMethods.Ping, StringComparison.Ordinal))
                {
                    if (request.Id is not null)
                    {
                        await WriteAsync(new RpcResponse(request.Id, ResultOf(new { })), _cts.Token)
                            .ConfigureAwait(false);
                    }

                    continue;
                }

                _ = HandleAsync(request);
            }
        }
        catch (FrameTooLargeException ex)
        {
            _logger.LogWarning("连接 {Connection} 帧超限：{Message}", State.DisplayName, ex.Message);
            await TryWriteAsync(
                    new RpcResponse(
                        null,
                        Error: new RpcError(RpcErrors.FrameTooLarge, $"帧超过 {ex.MaxBytes} 字节上限")),
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 正常关闭 / 空闲超时 / hello 超时
        }
        catch (IOException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "连接 {Connection} 异常退出", State.DisplayName);
        }
        finally
        {
            await DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>该连接是否有权收到这条事件。</summary>
    public bool ShouldReceive(string eventName, string? eventLaneId, bool fromRoleplayLane)
        => RoleFilter.CanReceive(State.Role, State.LaneId, eventName, eventLaneId, fromRoleplayLane);

    /// <summary>推一条事件给该连接。</summary>
    public Task SendEventAsync(string eventName, JsonElement payload, CancellationToken cancellationToken)
        => WriteAsync(new RpcNotification(eventName, payload), cancellationToken);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _hub.Detach(Id);
        await _cts.CancelAsync().ConfigureAwait(false);
        _stream.Dispose();
        _socket.Dispose();
        _writeLock.Dispose();
        _cts.Dispose();
    }

    private async Task<byte[]?> ReadFrameAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        timeout.CancelAfter(State.HelloCompleted ? RpcProtocol.IdleTimeout : RpcProtocol.HelloTimeout);
        try
        {
            return await FrameCodec.ReadAsync(_stream, cancellationToken: timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!_cts.IsCancellationRequested)
        {
            // hello 超时或空闲超时：静默断开
            return null;
        }
        catch (EndOfStreamException)
        {
            return null;
        }
    }

    private async Task HandleAsync(RpcRequest request)
    {
        try
        {
            var wasHelloCompleted = State.HelloCompleted;
            var response = await _dispatcher.HandleAsync(request, State, _cts.Token).ConfigureAwait(false);

            // 先登记再回包：客户端拿到 hello 结果时一定能收到后续事件
            if (!wasHelloCompleted && State.HelloCompleted)
            {
                _logger.LogInformation("客户端已连接：{Connection}", State.DisplayName);
                _hub.Attach(this);
            }

            await WriteAsync(response, _cts.Token).ConfigureAwait(false);

            if (response.Error?.Code == RpcErrors.IncompatibleVersion)
            {
                await DisposeAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "请求处理失败：{Method}", request.Method);
        }
    }

    private async Task WriteErrorAsync(JsonElement? id, int code, string message, string? detail = null)
    {
        object? data = detail is null ? null : new { detail };
        JsonElement? dataElement = data is null
            ? null
            : JsonSerializer.SerializeToElement(data, data.GetType(), JsonWire.Options);
        await TryWriteAsync(
                new RpcResponse(id, Error: new RpcError(code, message, dataElement)),
                CancellationToken.None)
            .ConfigureAwait(false);
    }

    private async Task TryWriteAsync(object envelope, CancellationToken cancellationToken)
    {
        try
        {
            await WriteAsync(envelope, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // 连接已断，错误包发不出去也无所谓
        }
    }

    private async Task WriteAsync(object envelope, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, envelope.GetType(), JsonWire.Options);
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await FrameCodec.WriteAsync(_stream, bytes, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private static JsonElement ResultOf(object value)
        => JsonSerializer.SerializeToElement(value, value.GetType(), JsonWire.Options);
}
