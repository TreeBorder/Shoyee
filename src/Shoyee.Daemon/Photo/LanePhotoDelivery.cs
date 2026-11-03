using Microsoft.Extensions.Logging;
using Shoyee.Channels.Delivery;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Rpc;
using Shoyee.Gateway.Subscriptions;
using Shoyee.Photo;
using Shoyee.Storage.Abstractions;

namespace Shoyee.Daemon.Photo;

/// <summary>
/// 出图完成的投递：桌面推 <c>photo.ready</c> 事件；IM 走投递台账**传文件**。
///
/// - 不伪造历史消息：图只是「发出去」，会话正文仍是她说的话；
/// - 桌面给本地绝对路径（同机直读），IM 传文件而不是引用 ComfyUI 的 <c>view</c> 链接
///   （否则历史图绑死在 Comfy 生命周期，也会把 ComfyUI 暴露出去）；
/// - 目标地址取 Lane 最近一次入站的 <c>last_target</c>。桌面发起的出图、或该 Lane
///   还没有过入站（没有 target）时，只推事件、不发 IM。
/// </summary>
public sealed class LanePhotoDelivery : IPhotoDelivery
{
    private readonly EventHub _events;
    private readonly ILaneRepository _lanes;
    private readonly ReliableDelivery _delivery;
    private readonly ILogger<LanePhotoDelivery> _logger;
    private readonly IMessageRepository? _messages;

    /// <summary>创建投递。</summary>
    /// <param name="events">事件总线。</param>
    /// <param name="lanes">Lane 仓库（取回投地址）。</param>
    /// <param name="delivery">带台账的出站投递（IM 传文件）。</param>
    /// <param name="logger">日志。</param>
    /// <param name="messages">消息投影仓库（把异步图写回工具结果，供桌面回看）；null = 不写回。</param>
    public LanePhotoDelivery(
        EventHub events,
        ILaneRepository lanes,
        ReliableDelivery delivery,
        ILogger<LanePhotoDelivery> logger,
        IMessageRepository? messages = null)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(lanes);
        ArgumentNullException.ThrowIfNull(delivery);
        ArgumentNullException.ThrowIfNull(logger);
        _events = events;
        _lanes = lanes;
        _delivery = delivery;
        _logger = logger;
        _messages = messages;
    }

    /// <inheritdoc />
    public async Task DeliverAsync(PhotoDelivery delivery, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(delivery);

        await PersistToToolResultAsync(delivery, cancellationToken).ConfigureAwait(false);

        var payload = new PhotoReadyEvent(
            delivery.Context.TaskId,
            delivery.Context.SessionId,
            delivery.Assets.Select(static a => new PhotoAssetView(a.Path, a.Mime, a.SizeBytes)).ToArray(),
            delivery.Context.Caption,
            delivery.Context.LaneId);

        await _events.PublishAsync(RpcEvents.PhotoReady, payload, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation(
            "角色图已投递到桌面事件：{TaskId} → {LaneId}（{Count} 张）",
            delivery.Context.TaskId,
            delivery.Context.LaneId,
            delivery.Assets.Count);

        await DeliverToChannelAsync(delivery, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 把图写回发起它的 <c>camera</c> / <c>gallery</c> 工具结果（结果文本里带任务 id），
    /// 桌面重新载入历史时仍能看到。轮次投影可能比出图晚落库，所以这里短等重试几次。
    /// </summary>
    private async Task PersistToToolResultAsync(PhotoDelivery delivery, CancellationToken cancellationToken)
    {
        if (_messages is null || delivery.Assets.Count == 0)
        {
            return;
        }

        var attachments = delivery.Assets
            .Select(static a => new MessageAttachment(
                a.Path,
                Path.GetFileName(a.Path),
                a.Mime,
                a.SizeBytes,
                AttachmentKind.Image))
            .ToList();

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var updated = await _messages
                .AttachToToolResultAsync(delivery.Context.LaneId, delivery.Context.TaskId, attachments, cancellationToken)
                .ConfigureAwait(false);
            if (updated is not null)
            {
                _logger.LogInformation(
                    "出图已写回工具结果：{TaskId} → message {MessageId}",
                    delivery.Context.TaskId,
                    updated.Id);
                return;
            }

            if (attempt < 2)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            }
        }

        _logger.LogWarning(
            "出图找不到对应的工具结果，图不会进历史：{TaskId}（Lane {LaneId}）",
            delivery.Context.TaskId,
            delivery.Context.LaneId);
    }

    private async Task DeliverToChannelAsync(PhotoDelivery delivery, CancellationToken cancellationToken)
    {
        var lane = await _lanes.GetAsync(delivery.Context.LaneId, cancellationToken).ConfigureAwait(false);
        if (lane is null || lane.Channel == LaneChannel.Desktop)
        {
            return; // 桌面同机读路径，不用再传
        }

        if (string.IsNullOrEmpty(lane.LastTarget))
        {
            _logger.LogInformation(
                "Lane {LaneId} 还没有入站过（没有回投地址），这次只推桌面事件",
                lane.Id);
            return;
        }

        for (var i = 0; i < delivery.Assets.Count; i++)
        {
            await _delivery
                .SendFileAsync(
                    lane.Id,
                    delivery.Context.SessionId,
                    lane.LastTarget,
                    delivery.Assets[i].Path,
                    i == 0 ? delivery.Context.Caption : null,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
