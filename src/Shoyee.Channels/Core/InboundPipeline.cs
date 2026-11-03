using Microsoft.Extensions.Logging;
using Shoyee.Channels.Delivery;
using Shoyee.Contracts.Abstractions;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Rpc;
using Shoyee.Handlers;
using Shoyee.Handlers.Commands;
using Shoyee.Storage.Abstractions;

namespace Shoyee.Channels.Core;

/// <summary>
/// 入站管线的唯一实现：规范化消息 → Lane 指针 → 轮次。
///
/// 分工（别在这里加提示词/工具判断）：
/// - 工作模式：Lane 的 <c>active_session_id</c> 就是「IM 当前会话」；没有就建一个并设为当前；
/// - RP：没有会话概念，直接按 Lane 投一轮；
/// - 失败回一条短提示给用户（走投递台账），并把详情留在日志里。
/// </summary>
public sealed class InboundPipeline : IInboundSink
{
    private readonly ILaneRepository _lanes;
    private readonly ISessionService _sessions;
    private readonly ITurnService _turns;
    private readonly ReliableDelivery _delivery;
    private readonly CommandRouter _commands;
    private readonly IReadOnlyDictionary<string, IChannelAdapter> _adapters;
    private readonly InboundDeduper _deduper;
    private readonly ILogger<InboundPipeline> _logger;

    /// <summary>创建管线。</summary>
    /// <param name="lanes">Lane 仓库。</param>
    /// <param name="sessions">会话领域服务（工作模式建会话）。</param>
    /// <param name="turns">轮次服务。</param>
    /// <param name="delivery">出站投递（失败提示）。</param>
    /// <param name="commands">预置指令路由（/approve /reject /stop /help 不进模型）。</param>
    /// <param name="adapters">渠道适配器（只用来发 typing）。</param>
    /// <param name="deduper">入站去重。</param>
    /// <param name="logger">日志。</param>
    public InboundPipeline(
        ILaneRepository lanes,
        ISessionService sessions,
        ITurnService turns,
        ReliableDelivery delivery,
        CommandRouter commands,
        IEnumerable<IChannelAdapter> adapters,
        InboundDeduper deduper,
        ILogger<InboundPipeline> logger)
    {
        ArgumentNullException.ThrowIfNull(lanes);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(turns);
        ArgumentNullException.ThrowIfNull(delivery);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(adapters);
        ArgumentNullException.ThrowIfNull(deduper);
        ArgumentNullException.ThrowIfNull(logger);
        _lanes = lanes;
        _sessions = sessions;
        _turns = turns;
        _delivery = delivery;
        _commands = commands;
        _adapters = adapters.ToDictionary(static a => a.LaneId, StringComparer.Ordinal);
        _deduper = deduper;
        _logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask OnInboundAsync(InboundMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!_deduper.TryAdd(message.LaneId, message.ExternalId))
        {
            _logger.LogDebug("重复入站，跳过：{LaneId} / {ExternalId}", message.LaneId, message.ExternalId);
            return;
        }

        string? sessionId = null;
        try
        {
            var lane = await _lanes.GetAsync(message.LaneId, cancellationToken).ConfigureAwait(false);
            if (lane is null)
            {
                _logger.LogWarning("入站消息指向未播种的 Lane：{LaneId}", message.LaneId);
                return;
            }

            // 入站同时刷新 lastInteractionAt：心跳/思念的唯一依据（跨模式跨渠道）
            await _lanes
                .TouchInboundAsync(message.LaneId, message.ReceivedAt, message.Target, cancellationToken)
                .ConfigureAwait(false);

            // 用户来消息 = 发送窗口可能刚打开（微信）；把挂着的补发掉，再处理这一条
            await _delivery.FlushLaneAsync(message.LaneId, cancellationToken).ConfigureAwait(false);

            // 预置指令是确定性入口：/approve /reject 必须在进模型之前截住，否则会被当成普通聊天
            if (_commands.IsKnownCommand(message.Text))
            {
                await DispatchCommandAsync(lane, message, cancellationToken).ConfigureAwait(false);
                return;
            }

            SendTyping(message);

            var text = message.Text;
            var attachments = ToAttachments(message.Attachments);
            TurnSendRequest request;
            if (lane.Mode == Mode.Work)
            {
                sessionId = await ResolveWorkSessionAsync(lane, cancellationToken).ConfigureAwait(false);
                request = new TurnSendRequest(
                    text, SessionId: sessionId, Attachments: attachments, ReplyTarget: message.Target);
            }
            else
            {
                request = new TurnSendRequest(
                    text, LaneId: message.LaneId, Attachments: attachments, ReplyTarget: message.Target);
            }

            // 回投地址随请求进事件（turn.*），不靠 FIFO 猜：主动消息的轮次没有地址，渠道就不会误回帖
            await _turns.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 停机过程，不当作失败
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "入站处理失败：{LaneId} ← {Sender}（{ExternalId}）",
                message.LaneId,
                message.SenderId,
                message.ExternalId);
            await NotifyFailureAsync(message, sessionId, ex).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 指令路径：不建会话、不跑模型，只落副作用 + 回一句结果。
    ///
    /// 工作模式定位到 Lane 的当前会话（没有就是「没有待审批/没有在跑的轮次」）；
    /// RP 没有会话，定位到 Lane 本身。
    /// </summary>
    private async Task DispatchCommandAsync(
        LaneRecord lane,
        InboundMessage message,
        CancellationToken cancellationToken)
    {
        var isWork = lane.Mode == Mode.Work;
        var target = isWork ? lane.ActiveSessionId ?? lane.Id : lane.Id;
        var context = new CommandContext(lane.Id, target, string.Empty, lane.Mode);
        var reply = await _commands.RouteAsync(context, message.Text, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "指令 {Command}：{LaneId} → {Target}",
            CommandRouter.ParseCommandName(message.Text),
            lane.Id,
            target);

        if (string.IsNullOrWhiteSpace(reply))
        {
            return;
        }

        await _delivery
            .SendTextAsync(lane.Id, isWork ? lane.ActiveSessionId : null, message.Target, reply, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>工作模式：取 Lane 的当前会话；没有就建一个并设为当前。</summary>
    private async Task<string> ResolveWorkSessionAsync(LaneRecord lane, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(lane.ActiveSessionId))
        {
            return lane.ActiveSessionId;
        }

        var created = await _sessions
            .CreateAsync(new SessionCreateRequest(Mode.Work, lane.Id), cancellationToken)
            .ConfigureAwait(false);
        await _sessions.SetActiveForLaneAsync(created.Id, lane.Id, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("为 {LaneId} 建了工作会话 {SessionId}", lane.Id, created.Id);
        return created.Id;
    }

    /// <summary>
    /// 正文组装：附件以「本地路径」逐行附在正文后。
    ///
    /// 现在没有 asset 表，<see cref="TurnSendRequest.Attachments"/> 的 <c>AssetId</c> 无处可指；
    /// 等附件真正进入模型上下文（视觉/转写）时再改成结构化入参，别先造空壳 id。
    /// </summary>
    /// <summary>渠道附件 → 消息附件的展示类别（路径是唯一真源，模型与桌面都读本地文件）。</summary>
    private static IReadOnlyList<MessageAttachment>? ToAttachments(IReadOnlyList<InboundAttachment>? attachments)
        => attachments is { Count: > 0 }
            ? attachments.Select(static attachment => new MessageAttachment(
                attachment.LocalPath,
                attachment.FileName,
                attachment.Mime,
                attachment.SizeBytes,
                attachment.Kind switch
                {
                    InboundAttachmentKind.Image => AttachmentKind.Image,
                    InboundAttachmentKind.Audio => AttachmentKind.Audio,
                    _ => AttachmentKind.File,
                })).ToList()
            : null;

    /// <summary>typing 是锦上添花：失败只记日志，不挡这一轮。</summary>
    private void SendTyping(InboundMessage message)
    {
        if (!_adapters.TryGetValue(message.LaneId, out var adapter) || adapter is not ITypingChannel typing)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await typing.SendTypingAsync(message.Target, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "typing 信号失败：{LaneId}", message.LaneId);
            }
        });
    }

    private async Task NotifyFailureAsync(InboundMessage message, string? sessionId, Exception error)
    {
        var text = error is RpcException rpc
            ? $"（这一轮没跑起来：{rpc.Message}）"
            : "（这一轮没跑起来，详情见 daemon 日志。）";
        try
        {
            await _delivery
                .SendTextAsync(message.LaneId, sessionId, message.Target, text, cancellationToken: CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "失败提示也没能发出：{LaneId}", message.LaneId);
        }
    }
}
