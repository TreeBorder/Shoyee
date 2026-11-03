using Shoyee.Contracts.Dtos;

namespace Shoyee.Contracts.Abstractions;

/// <summary>
/// 入站管线入口：渠道适配器把规范化后的消息交给它。
///
/// 管线负责去重、Lane 指针、建/开工作会话、调 <see cref="ITurnService"/>；
/// 适配器不应自己拼提示词、也不应直接调 Agent。
/// </summary>
public interface IInboundSink
{
    /// <summary>投递一条入站消息；不抛异常（失败在管线内记录并回一条提示）。</summary>
    /// <param name="message">规范化消息。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    ValueTask OnInboundAsync(InboundMessage message, CancellationToken cancellationToken = default);
}
