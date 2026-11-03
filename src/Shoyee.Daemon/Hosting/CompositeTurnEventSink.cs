using Microsoft.Extensions.Logging;
using Shoyee.Channels.Core;
using Shoyee.Contracts.Abstractions;
using Shoyee.Gateway.Subscriptions;

namespace Shoyee.Daemon.Hosting;

/// <summary>
/// 轮次事件的三路出口：桌面（<see cref="EventHub"/>）、IM 回帖（<see cref="ChannelReplyRouter"/>）、
/// IM 审批提示（<see cref="ChannelApprovalRouter"/>）。
///
/// 每一路都要收到，且任何一路失败都不能把别的路拖死——事件是旁路，不是关键路径；
/// 回帖与审批提示本身还有投递台账兜底，这里只需要不让异常冒进 Agent。
/// </summary>
internal sealed class CompositeTurnEventSink : ITurnEventSink
{
    private readonly EventHub _hub;
    private readonly ChannelReplyRouter _channels;
    private readonly ChannelApprovalRouter _approvals;
    private readonly ILogger<CompositeTurnEventSink> _logger;

    public CompositeTurnEventSink(
        EventHub hub,
        ChannelReplyRouter channels,
        ChannelApprovalRouter approvals,
        ILogger<CompositeTurnEventSink> logger)
    {
        _hub = hub;
        _channels = channels;
        _approvals = approvals;
        _logger = logger;
    }

    public async ValueTask PublishAsync(
        string eventName,
        object payload,
        CancellationToken cancellationToken = default)
    {
        await ForwardAsync(_hub, "桌面事件推送", eventName, payload, cancellationToken).ConfigureAwait(false);
        await ForwardAsync(_channels, "IM 回帖", eventName, payload, cancellationToken).ConfigureAwait(false);
        await ForwardAsync(_approvals, "IM 审批提示", eventName, payload, cancellationToken).ConfigureAwait(false);
    }

    private async Task ForwardAsync(
        ITurnEventSink sink,
        string description,
        string eventName,
        object payload,
        CancellationToken cancellationToken)
    {
        try
        {
            await sink.PublishAsync(eventName, payload, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Description}失败：{Event}", description, eventName);
        }
    }
}
