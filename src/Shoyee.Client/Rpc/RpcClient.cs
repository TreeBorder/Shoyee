using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text.Json;
using Shoyee.Client.Transport;
using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Rpc;

namespace Shoyee.Client.Rpc;

/// <summary>服务端返回的 JSON-RPC 错误（客户端侧表示）。</summary>
public sealed class RpcClientException : Exception
{
    /// <summary>创建异常。</summary>
    /// <param name="code">错误码。</param>
    /// <param name="message">错误消息。</param>
    /// <param name="data">附加数据。</param>
    public RpcClientException(int code, string message, JsonElement? data = null)
        : base(message)
    {
        Code = code;
        ErrorData = data;
    }

    /// <summary>错误码（<see cref="RpcErrors"/>）。</summary>
    public int Code { get; }

    /// <summary>附加数据。</summary>
    public JsonElement? ErrorData { get; }
}

/// <summary>
/// 桌面 / ctl 共用的 UDS JSON-RPC 客户端：连接 → hello → 请求/响应 + 事件回调。
/// 请求 id 由客户端自增，响应按 id 配对；事件（无 id 的 notification）走 <see cref="EventReceived"/>。
/// </summary>
public sealed class RpcClient : IAsyncDisposable
{
    private readonly Socket _socket;
    private readonly NetworkStream _stream;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentDictionary<long, TaskCompletionSource<RpcResponse>> _pending = new();
    private readonly Task _readLoop;
    private long _nextId;
    private int _disposed;

    private RpcClient(Socket socket)
    {
        _socket = socket;
        _stream = new NetworkStream(socket, ownsSocket: false);
        _readLoop = Task.Run(ReadLoopAsync);
    }

    /// <summary>收到服务端事件（事件名，载荷）。在读取循环线程上同步调用，回调要快、要自己 try/catch。</summary>
    public event Action<string, JsonElement>? EventReceived;

    /// <summary>握手返回的服务器信息（<see cref="ConnectAsync"/> 完成前为 null）。</summary>
    public HelloResult Hello { get; private set; } = null!;

    /// <summary>连接是否还在（读取循环未退出、未 dispose）。</summary>
    public bool IsConnected => Volatile.Read(ref _disposed) == 0 && !_readLoop.IsCompleted;

    /// <summary>连接并完成 hello 握手。</summary>
    /// <param name="socketPath">UDS 路径。</param>
    /// <param name="hello">握手参数。</param>
    /// <param name="timeout">整段连接 + 握手的超时（null = 默认 5s）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public static async Task<RpcClient> ConnectAsync(
        string socketPath,
        HelloRequest hello,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(socketPath);
        ArgumentNullException.ThrowIfNull(hello);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout ?? RpcProtocol.HelloTimeout);

        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), deadline.Token).ConfigureAwait(false);
            var client = new RpcClient(socket);
            try
            {
                client.Hello = await client.CallAsync<HelloResult>(RpcMethods.Hello, hello, deadline.Token)
                    .ConfigureAwait(false);
                return client;
            }
            catch
            {
                await client.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>发一个请求并等响应。</summary>
    /// <param name="method">方法名（<see cref="RpcMethods"/>）。</param>
    /// <param name="parameters">参数对象；null = 不带 params。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<JsonElement> CallAsync(
        string method,
        object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        var response = await SendAsync(method, parameters, expectResponse: true, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("请求没有响应");
        if (response.Error is { } error)
        {
            throw new RpcClientException(error.Code, error.Message, error.Data);
        }

        return response.Result ?? default;
    }

    /// <summary>发请求并按类型反序列化结果。</summary>
    /// <typeparam name="T">结果类型。</typeparam>
    /// <param name="method">方法名。</param>
    /// <param name="parameters">参数对象。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<T> CallAsync<T>(
        string method,
        object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var result = await CallAsync(method, parameters, cancellationToken).ConfigureAwait(false);
        if (result.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return default!;
        }

        return result.Deserialize<T>(JsonWire.Options)
               ?? throw new RpcClientException(RpcErrors.Internal, "响应结果无法反序列化");
    }

    /// <summary>发 <c>rpc.ping</c>（notification，不等响应）。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task PingAsync(CancellationToken cancellationToken = default)
        => await SendAsync(RpcMethods.Ping, parameters: null, expectResponse: false, cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _cts.CancelAsync().ConfigureAwait(false);
        try
        {
            await _readLoop.ConfigureAwait(false);
        }
        catch
        {
            // 读循环退出异常与关闭无关
        }

        foreach (var pending in _pending.Values)
        {
            pending.TrySetException(new ObjectDisposedException(nameof(RpcClient)));
        }

        _pending.Clear();
        _stream.Dispose();
        _socket.Dispose();
        _writeLock.Dispose();
        _cts.Dispose();
    }

    private async Task<RpcResponse?> SendAsync(
        string method,
        object? parameters,
        bool expectResponse,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        TaskCompletionSource<RpcResponse>? completion = null;
        JsonElement? id = null;
        long numericId = 0;
        if (expectResponse)
        {
            numericId = Interlocked.Increment(ref _nextId);
            id = JsonSerializer.SerializeToElement(numericId);
            completion = new TaskCompletionSource<RpcResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[numericId] = completion;
        }

        try
        {
            var request = new RpcRequest(
                method,
                parameters is null
                    ? null
                    : JsonSerializer.SerializeToElement(parameters, parameters.GetType(), JsonWire.Options),
                id);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(request, JsonWire.Options);
            await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await FrameCodec.WriteAsync(_stream, bytes, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }

            if (completion is null)
            {
                return null;
            }

            using var registration = cancellationToken.Register(
                static state => ((TaskCompletionSource<RpcResponse>)state!).TrySetCanceled(),
                completion);
            return await completion.Task.ConfigureAwait(false);
        }
        catch
        {
            if (completion is not null)
            {
                _pending.TryRemove(numericId, out _);
            }

            throw;
        }
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var frame = await FrameCodec.ReadAsync(_stream, cancellationToken: _cts.Token).ConfigureAwait(false);
                if (frame is null)
                {
                    break;
                }

                using var document = JsonDocument.Parse(frame);
                var root = document.RootElement;
                if (root.TryGetProperty("method", out var methodElement)
                    && methodElement.ValueKind == JsonValueKind.String
                    && !root.TryGetProperty("id", out _))
                {
                    var name = methodElement.GetString()!;
                    var payload = root.TryGetProperty("params", out var paramsElement)
                        ? paramsElement.Clone()
                        : default;
                    try
                    {
                        EventReceived?.Invoke(name, payload);
                    }
                    catch
                    {
                        // 客户端回调异常不能杀死读循环
                    }

                    continue;
                }

                if (!root.TryGetProperty("id", out var idElement) || idElement.ValueKind != JsonValueKind.Number)
                {
                    continue;
                }

                if (_pending.TryRemove(idElement.GetInt64(), out var completion))
                {
                    var response = root.Deserialize<RpcResponse>(JsonWire.Options)!;
                    completion.TrySetResult(response);
                }
            }
        }
        catch
        {
            // 断开或解析失败：统一按连接结束处理
        }
        finally
        {
            foreach (var pending in _pending.Values)
            {
                pending.TrySetException(new IOException("连接已断开"));
            }

            _pending.Clear();
        }
    }
}
