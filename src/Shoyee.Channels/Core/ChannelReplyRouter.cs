using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;
using Shoyee.Channels.Delivery;
using Shoyee.Contracts.Abstractions;
using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Rpc;
using Shoyee.Contracts.Text;

namespace Shoyee.Channels.Core;

/// <summary>
/// 回帖：把一轮的助手正文送回它来的那条 IM。
///
/// 去向**由轮次事件携带**（<c>turn.*.replyTarget</c>，入站时写进 <see cref="TurnSendRequest"/>）：
/// 主动消息（心跳/cron）的轮次没有地址，所以不会被误回帖；也不再按 Lane+会话 FIFO 猜对位
/// ——主动轮次插进来会把用户的去向挤错。
///
/// 这是事件**旁路**——它不负责转发给桌面（那是 Gateway 的 EventHub），
/// Daemon 把两者包成一个组合 sink。按轮末整条发，不逐 delta 发：
/// Mattermost/微信都是「一条消息一次送达」的体验，逐段刷会把长回答拆成碎片，
/// 微信还会把窗口配额打满。
/// </summary>
public sealed class ChannelReplyRouter : ITurnEventSink
{
    private readonly ReliableDelivery _delivery;
    private readonly IReadOnlyDictionary<string, IChannelAdapter> _adapters;
    private readonly ILogger<ChannelReplyRouter> _logger;
    private readonly ConcurrentDictionary<string, StringBuilder> _buffers = new(StringComparer.Ordinal);

    /// <summary>创建回帖旁路。</summary>
    /// <param name="delivery">投递（带台账）。</param>
    /// <param name="adapters">渠道适配器（回合结束停 typing）。</param>
    /// <param name="logger">日志。</param>
    public ChannelReplyRouter(
        ReliableDelivery delivery,
        IEnumerable<IChannelAdapter> adapters,
        ILogger<ChannelReplyRouter> logger)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        ArgumentNullException.ThrowIfNull(adapters);
        ArgumentNullException.ThrowIfNull(logger);
        _delivery = delivery;
        _adapters = adapters.ToDictionary(static a => a.LaneId, StringComparer.Ordinal);
        _logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask PublishAsync(
        string eventName,
        object payload,
        CancellationToken cancellationToken = default)
    {
        switch (eventName)
        {
            case RpcEvents.TurnDelta when payload is TurnDeltaEvent delta:
                _buffers.GetOrAdd(delta.TurnId, static _ => new StringBuilder()).Append(delta.Delta);
                break;

            case RpcEvents.TurnCompleted when payload is TurnCompletedEvent completed:
                await FinishAsync(
                        completed.TurnId,
                        completed.LaneId,
                        completed.SessionId,
                        completed.ReplyTarget,
                        null,
                        completed.Attachments,
                        cancellationToken)
                    .ConfigureAwait(false);
                break;

            case RpcEvents.TurnFailed when payload is TurnFailedEvent failed:
                await FinishAsync(
                        failed.TurnId,
                        failed.LaneId,
                        failed.SessionId,
                        failed.ReplyTarget,
                        $"（这一轮没跑完：{failed.Message}）",
                        null,
                        cancellationToken)
                    .ConfigureAwait(false);
                break;
        }
    }

    private async Task FinishAsync(
        string turnId,
        string? laneId,
        string? eventSessionId,
        string? replyTarget,
        string? text,
        IReadOnlyList<MessageAttachment>? attachments,
        CancellationToken cancellationToken)
    {
        _buffers.TryRemove(turnId, out var buffer);

        // 桌面发起的轮次、主动消息的轮次都没有回投地址，这里直接跳过
        if (string.IsNullOrEmpty(laneId) || string.IsNullOrEmpty(replyTarget))
        {
            return;
        }

        await StopTypingAsync(laneId, replyTarget, cancellationToken).ConfigureAwait(false);

        var body = MessageText.CleanReply(text ?? buffer?.ToString());
        if (body.Length == 0 && attachments is not { Count: > 0 })
        {
            return;
        }

        // RP 的事件 sessionId 是 Lane id 占位；工作模式才是真会话
        var sessionId = string.Equals(eventSessionId, laneId, StringComparison.Ordinal)
            ? null
            : eventSessionId;

        if (body.Length > 0)
        {
            await _delivery
                .SendTextAsync(laneId, sessionId, replyTarget, body, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            _logger.LogInformation("已回帖到 {LaneId} / {Target}（{Length} 字）", laneId, replyTarget, body.Length);
        }

        // 出站附件（语音/图片/文件）：正文之后逐个传文件，每张单独一条台账
        if (attachments is { Count: > 0 })
        {
            foreach (var attachment in attachments)
            {
                await _delivery
                    .SendFileAsync(
                        laneId,
                        sessionId,
                        replyTarget,
                        attachment.Path,
                        caption: null,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }

            _logger.LogInformation(
                "已回传 {Count} 个附件到 {LaneId} / {Target}",
                attachments.Count,
                laneId,
                replyTarget);
        }
    }

    /// <summary>回合结束停 typing（成对调用；渠道自动过期的实现是空操作）。</summary>
    private async Task StopTypingAsync(string laneId, string target, CancellationToken cancellationToken)
    {
        if (!_adapters.TryGetValue(laneId, out var adapter) || adapter is not ITypingChannel typing)
        {
            return;
        }

        try
        {
            await typing.StopTypingAsync(target, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "停止 typing 失败：{LaneId}", laneId);
        }
    }
}
