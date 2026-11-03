using Shoyee.Contracts.Domain;

namespace Shoyee.Contracts.Abstractions;

/// <summary>
/// 一个渠道账号的入站适配器（== 一条 Lane）。
///
/// 约定：
/// - <see cref="RunAsync"/> 内部自己重连，只在 <paramref name="cancellationToken"/> 取消时返回；
/// - 发送不走这里，出站由 <see cref="IChannelSender"/> 实现；
/// - 适配器只依赖 Contracts，不引用 <c>Shoyee.Channels</c>，由 Daemon 组合根接进管线。
/// </summary>
public interface IChannelAdapter
{
    /// <summary>该适配器负责的 Lane id。</summary>
    string LaneId { get; }

    /// <summary>当前连接状态。</summary>
    ChannelConnectionState State { get; }

    /// <summary>最近一次连接错误（成功连接后清空）。</summary>
    string? LastError { get; }

    /// <summary>跑接收循环；取消时正常返回。</summary>
    /// <param name="sink">入站管线。</param>
    /// <param name="cancellationToken">停止信号。</param>
    Task RunAsync(IInboundSink sink, CancellationToken cancellationToken);
}
