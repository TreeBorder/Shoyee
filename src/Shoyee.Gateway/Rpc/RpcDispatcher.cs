using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shoyee.Contracts.Rpc;

namespace Shoyee.Gateway.Rpc;

/// <summary>
/// JSON-RPC 分发：方法存在性 → 角色权限 → 参数解析 → 调用 → 响应/错误。
/// 业务错误用 <see cref="RpcException"/>（带契约错误码），未捕获异常一律折成 <c>internal</c> 并带 traceId。
/// </summary>
public sealed class RpcDispatcher
{
    private readonly IReadOnlyDictionary<string, RpcHandler> _handlers;
    private readonly ILogger<RpcDispatcher> _logger;

    /// <summary>创建分发器。</summary>
    /// <param name="services">方法依赖。</param>
    /// <param name="gate">停机闸门。</param>
    /// <param name="control">停机入口。</param>
    /// <param name="logger">日志。</param>
    public RpcDispatcher(
        RpcServices services,
        ShutdownGate gate,
        DaemonControl control,
        ILogger<RpcDispatcher>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(control);
        _handlers = RpcHandlers.Build(services, gate, control);
        _logger = logger ?? NullLogger<RpcDispatcher>.Instance;
    }

    /// <summary>已注册的方法数（测试/诊断用）。</summary>
    public int HandlerCount => _handlers.Count;

    /// <summary>处理一条请求并产出响应（不会抛业务异常）。</summary>
    /// <param name="request">请求信封。</param>
    /// <param name="connection">连接状态。</param>
    /// <param name="cancellationToken">取消令牌（连接断开/服务停止时触发）。</param>
    public async Task<RpcResponse> HandleAsync(
        RpcRequest request,
        RpcConnectionState connection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(connection);
        var id = request.Id;

        if (!connection.HelloCompleted && !string.Equals(request.Method, RpcMethods.Hello, StringComparison.Ordinal))
        {
            return Error(id, RpcErrors.UnauthorizedRole, "第一帧必须是 rpc.hello", null);
        }

        if (!_handlers.TryGetValue(request.Method, out var handler))
        {
            return Error(id, RpcErrors.MethodNotFound, $"方法未实现：{request.Method}", new { method = request.Method });
        }

        if (!RoleFilter.CanCall(connection.Role, request.Method))
        {
            return Error(
                id,
                RpcErrors.UnauthorizedRole,
                $"角色 {connection.Role} 不能调用 {request.Method}",
                new { method = request.Method, role = connection.Role?.ToString() });
        }

        var call = new RpcCall(request.Params, connection);
        try
        {
            var result = await handler(call, cancellationToken).ConfigureAwait(false);
            return new RpcResponse(id, ResultOf(result));
        }
        catch (OperationCanceledException)
        {
            // 连接断开 / daemon 停机：交给连接层静默收尾
            throw;
        }
        catch (RpcException ex)
        {
            return Error(id, ex.Code, ex.Message, ex.ErrorData);
        }
        catch (Exception ex)
        {
            var traceId = Guid.NewGuid().ToString("N");
            _logger.LogError(ex, "RPC {Method} 内部错误 traceId={TraceId}", request.Method, traceId);
            return Error(id, RpcErrors.Internal, "内部错误", new { traceId });
        }
    }

    private static JsonElement? ResultOf(object? result)
        => result switch
        {
            null => null,
            JsonElement element => element,
            _ => JsonSerializer.SerializeToElement(result, result.GetType(), JsonWire.Options),
        };

    private static RpcResponse Error(JsonElement? id, int code, string message, object? data)
    {
        JsonElement? dataElement = data switch
        {
            null => null,
            JsonElement element => element,
            _ => JsonSerializer.SerializeToElement(data, data.GetType(), JsonWire.Options),
        };
        return new RpcResponse(id, Error: new RpcError(code, message, dataElement));
    }
}
