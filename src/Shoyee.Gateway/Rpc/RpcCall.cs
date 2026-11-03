using System.Text.Json;
using Shoyee.Contracts.Rpc;

namespace Shoyee.Gateway.Rpc;

/// <summary>一次方法调用的上下文：原始参数 + 连接状态。</summary>
public sealed class RpcCall
{
    /// <summary>创建调用上下文。</summary>
    /// <param name="parameters">原始 params（可能为 null）。</param>
    /// <param name="connection">连接状态。</param>
    public RpcCall(JsonElement? parameters, RpcConnectionState connection)
    {
        Parameters = parameters;
        Connection = connection;
    }

    /// <summary>原始 params。</summary>
    public JsonElement? Parameters { get; }

    /// <summary>连接状态（角色、Lane）。</summary>
    public RpcConnectionState Connection { get; }

    /// <summary>把 params 反序列化成请求 DTO；不是对象或缺字段时抛 <c>invalid_params</c>。</summary>
    /// <typeparam name="T">请求类型。</typeparam>
    public T Params<T>()
        where T : class
    {
        if (Parameters is not { ValueKind: JsonValueKind.Object } element)
        {
            throw new RpcException(RpcErrors.InvalidParams, "params 必须是对象");
        }

        try
        {
            return element.Deserialize<T>(JsonWire.Options)
                   ?? throw new RpcException(RpcErrors.InvalidParams, "params 解析为空");
        }
        catch (JsonException ex)
        {
            throw new RpcException(RpcErrors.InvalidParams, "params 格式错误", new { detail = ex.Message });
        }
        catch (NotSupportedException ex)
        {
            throw new RpcException(RpcErrors.InvalidParams, "params 类型不支持", new { detail = ex.Message });
        }
    }

    /// <summary>读一个必填字符串字段（<c>{ "sessionId": "..." }</c> 这类单字段参数）。</summary>
    /// <param name="name">字段名。</param>
    public string RequireString(string name)
    {
        if (Parameters is not { ValueKind: JsonValueKind.Object } element
            || !element.TryGetProperty(name, out var value)
            || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new RpcException(RpcErrors.InvalidParams, $"{name} 不能为空");
        }

        return value.GetString()!;
    }
}
