namespace Shoyee.Contracts.Rpc;

/// <summary>
/// 带 JSON-RPC 错误码的业务异常：业务层抛、Gateway 直接映射成 <see cref="RpcError"/>，
/// 不靠字符串猜错误类型。
/// </summary>
public sealed class RpcException : Exception
{
    /// <summary>创建异常。</summary>
    /// <param name="code">错误码（<see cref="RpcErrors"/> 常量）。</param>
    /// <param name="message">给客户端看的消息。</param>
    /// <param name="data">附加数据（会原样进 <c>error.data</c>）。</param>
    public RpcException(int code, string message, object? data = null)
        : base(message)
    {
        Code = code;
        ErrorData = data;
    }

    /// <summary>错误码。</summary>
    public int Code { get; }

    /// <summary>附加数据。</summary>
    public object? ErrorData { get; }
}
