namespace Shoyee.Contracts.Abstractions;

/// <summary>
/// 轮次事件出口：Agent 只负责「发生了什么」，由 Gateway 实现广播、渠道实现转发。
///
/// 事件名用 <see cref="Rpc.RpcEvents"/> 常量，载荷是 <see cref="Dtos"/> 里的 record；
/// Agent 不引用 Gateway，所以这一层必须是纯契约。
/// </summary>
public interface ITurnEventSink
{
    /// <summary>推送一条事件（载荷按 <paramref name="eventName"/> 对应的 DTO 序列化）。</summary>
    /// <param name="eventName">事件名（<see cref="Rpc.RpcEvents"/> 常量）。</param>
    /// <param name="payload">事件载荷。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    ValueTask PublishAsync(string eventName, object payload, CancellationToken cancellationToken = default);
}
