using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shoyee.Contracts.Rpc;

/// <summary>
/// JSON-RPC 2.0 请求信封。<c>id</c> 允许数字或字符串，故用 <see cref="JsonElement"/>；
/// <c>params</c> 由分发器按方法名二次解析成具体 DTO。
/// </summary>
public sealed record RpcRequest(
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("params")] JsonElement? Params = null,
    [property: JsonPropertyName("id")] JsonElement? Id = null,
    [property: JsonPropertyName("jsonrpc")] string Jsonrpc = "2.0");

/// <summary>JSON-RPC 2.0 响应信封（成功 <see cref="Result"/>，失败 <see cref="Error"/>）。</summary>
public sealed record RpcResponse(
    [property: JsonPropertyName("id")] JsonElement? Id,
    [property: JsonPropertyName("result")] JsonElement? Result = null,
    [property: JsonPropertyName("error")] RpcError? Error = null,
    [property: JsonPropertyName("jsonrpc")] string Jsonrpc = "2.0");

/// <summary>JSON-RPC 2.0 事件（服务端 → 客户端，无 id）。</summary>
public sealed record RpcNotification(
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("params")] JsonElement Params,
    [property: JsonPropertyName("jsonrpc")] string Jsonrpc = "2.0");

/// <summary>JSON-RPC 错误对象。</summary>
public sealed record RpcError(
    [property: JsonPropertyName("code")] int Code,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("data")] JsonElement? Data = null)
{
    /// <summary>按错误码构造，名称取 <see cref="RpcErrors.NameOf"/>。</summary>
    public static RpcError From(int code, string message, JsonElement? data = null)
        => new(code, message, data);
}
