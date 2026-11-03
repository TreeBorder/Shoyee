using Shoyee.Contracts.Abstractions;

namespace Shoyee.Agent.Runtime;

/// <summary>
/// 默认事件出口：没有 Gateway（测试 / 单机脚本）时吞掉事件。
/// Gateway 注册自己的 <see cref="ITurnEventSink"/> 后会覆盖它。
/// </summary>
internal sealed class NullTurnEventSink : ITurnEventSink
{
    /// <inheritdoc />
    public ValueTask PublishAsync(string eventName, object payload, CancellationToken cancellationToken = default)
        => default;
}
