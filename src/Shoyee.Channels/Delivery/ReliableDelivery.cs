using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shoyee.Contracts.Abstractions;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Text;
using Shoyee.Storage.Abstractions;

namespace Shoyee.Channels.Delivery;

/// <summary>
/// 出站投递：先挂账再发，失败留给补发；发送成功才销账。
///
/// 幂等复用靠 <see cref="IDeliveryRepository.EnqueueAsync"/>（同 Lane + 同 kind + 同负载的 pending 复用），
/// 所以重试不会把同一句话发两遍——前提是负载逐字节相同。
/// </summary>
public sealed class ReliableDelivery : IOutboundDelivery
{
    /// <summary>重试上限；超过就 <see cref="Contracts.Domain.DeliveryState.Abandoned"/>。</summary>
    public const int MaxAttempts = 10;

    /// <summary>
    /// 窗口关闭/会话过期时挂多久：这段时间内不再尝试，等该 Lane 有新入站时由
    /// <see cref="FlushLaneAsync"/> 立刻补（微信窗口期就是这么打开的）。
    /// </summary>
    public static readonly TimeSpan WindowRetryDelay = TimeSpan.FromHours(2);

    private readonly Dictionary<string, IChannelSender> _senders;
    private readonly IDeliveryRepository _ledger;
    private readonly ILogger<ReliableDelivery> _logger;
    private readonly TimeProvider _clock;
    private readonly ILaneRepository? _lanes;
    private readonly ISendAttemptRepository? _attempts;
    private readonly ConcurrentDictionary<long, byte> _inFlight = new();
    private readonly SemaphoreSlim _flushGate = new(1, 1);

    /// <summary>创建投递层。</summary>
    /// <param name="senders">所有渠道的发送器（每条 Lane 一个）。</param>
    /// <param name="ledger">投递台账。</param>
    /// <param name="logger">日志。</param>
    /// <param name="clock">时间源（测试可注入）。</param>
    /// <param name="lanes">Lane 仓库（实测台账要「距上次入站多久」；可空 = 不记据）。</param>
    /// <param name="attempts">发送实测仓库（可空 = 不记据）。</param>
    public ReliableDelivery(
        IEnumerable<IChannelSender> senders,
        IDeliveryRepository ledger,
        ILogger<ReliableDelivery>? logger = null,
        TimeProvider? clock = null,
        ILaneRepository? lanes = null,
        ISendAttemptRepository? attempts = null)
    {
        ArgumentNullException.ThrowIfNull(senders);
        ArgumentNullException.ThrowIfNull(ledger);
        _senders = senders.ToDictionary(static s => s.LaneId, StringComparer.Ordinal);
        _ledger = ledger;
        _logger = logger ?? NullLogger<ReliableDelivery>.Instance;
        _clock = clock ?? TimeProvider.System;
        _lanes = lanes;
        _attempts = attempts;
    }

    /// <summary>该 Lane 是否有发送器（没有就不该挂账）。</summary>
    /// <param name="laneId">Lane id。</param>
    public bool CanSend(string laneId) => _senders.ContainsKey(laneId);

    /// <summary>
    /// 发一条文本；失败挂账待补发。
    /// RP Lane（Mattermost 等，微信除外）按单个换行拆成多条消息，每条独立挂账/重试；
    /// 工作 Lane 保持整条发送。
    /// </summary>
    /// <param name="laneId">目标 Lane。</param>
    /// <param name="sessionId">所属会话（可空）。</param>
    /// <param name="target">回投地址（渠道自定义）。</param>
    /// <param name="text">正文。</param>
    /// <param name="kind">台账种类（message / heartbeat / notify；默认 message）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task SendTextAsync(
        string laneId,
        string? sessionId,
        string target,
        string text,
        string kind = "message",
        CancellationToken cancellationToken = default)
    {
        var segments = await SplitForRoleplayAsync(laneId, text, cancellationToken).ConfigureAwait(false);
        foreach (var segment in segments)
        {
            await EnqueueAndAttemptAsync(
                    laneId,
                    sessionId,
                    kind,
                    DeliveryPayload.ForText(target, segment),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// RP Lane 按单个换行拆消息；微信和拿不到 Lane 时不拆。
    /// 没有换行/<c>&lt;br/&gt;</c> 的正文直接原样返回，不查库。
    /// </summary>
    private async Task<IReadOnlyList<string>> SplitForRoleplayAsync(
        string laneId,
        string text,
        CancellationToken cancellationToken)
    {
        if (_lanes is null
            || (text.IndexOf('\n') < 0 && text.IndexOf("<br", StringComparison.OrdinalIgnoreCase) < 0))
        {
            return [text];
        }

        var lane = await _lanes.GetAsync(laneId, cancellationToken).ConfigureAwait(false);
        if (lane is null || lane.Mode != Mode.Roleplay || lane.Channel == LaneChannel.WeChat)
        {
            return [text];
        }

        var segments = ReplySegments.Split(text);
        return segments.Count == 0 ? [text] : segments;
    }

    /// <summary>发一个本地文件；失败挂账待补发。</summary>
    /// <param name="laneId">目标 Lane。</param>
    /// <param name="sessionId">所属会话（可空）。</param>
    /// <param name="target">回投地址。</param>
    /// <param name="filePath">本地绝对路径。</param>
    /// <param name="caption">配文（可空）。</param>
    /// <param name="kind">台账种类（默认 file）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task SendFileAsync(
        string laneId,
        string? sessionId,
        string target,
        string filePath,
        string? caption = null,
        string kind = "file",
        CancellationToken cancellationToken = default)
        => EnqueueAndAttemptAsync(
            laneId,
            sessionId,
            kind,
            DeliveryPayload.ForFile(target, filePath, caption),
            cancellationToken);

    /// <summary>
    /// 该 Lane 刚收到消息（发送窗口打开）：把挂着的都试一遍，**无视退避时间**。
    /// 与周期补发共用一把门，避免同一条被两次发送。
    /// </summary>
    /// <param name="laneId">Lane id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task FlushLaneAsync(string laneId, CancellationToken cancellationToken = default)
    {
        if (!await _flushGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            var rows = await _ledger.GetPendingForLaneAsync(laneId, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (rows.Count > 0)
            {
                _logger.LogInformation("{LaneId} 有 {Count} 条挂账，窗口打开后补发", laneId, rows.Count);
            }

            foreach (var row in rows)
            {
                await AttemptOrSettleAsync(row, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _flushGate.Release();
        }
    }

    /// <summary>补发所有到期的挂账；由 <see cref="DeliveryRetryService"/> 周期调用。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        if (!await _flushGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return; // 上一轮还没跑完，别叠
        }

        try
        {
            var due = await _ledger.GetDueAsync(_clock.GetUtcNow(), cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            foreach (var row in due)
            {
                await AttemptOrSettleAsync(row, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _flushGate.Release();
        }
    }

    private async Task EnqueueAndAttemptAsync(
        string laneId,
        string? sessionId,
        string kind,
        DeliveryPayload payload,
        CancellationToken cancellationToken)
    {
        if (!_senders.ContainsKey(laneId))
        {
            // 没有发送器就不挂账：挂上去永远补不发，只会把表塞满
            _logger.LogWarning("没有 {LaneId} 的发送器，丢弃 {Kind} 投递", laneId, kind);
            return;
        }

        var row = await _ledger
            .EnqueueAsync(laneId, sessionId, kind, payload.ToJson(), cancellationToken)
            .ConfigureAwait(false);
        await AttemptAsync(row, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 补发入口：不可信渠道的残留（崩溃前挂上、没来得及发）直接按已送达销账——
    /// 它的结果本来就不可验证，重发可能让用户收到两遍。
    /// </summary>
    private async Task AttemptOrSettleAsync(DeliveryRecord row, CancellationToken cancellationToken)
    {
        if (_senders.TryGetValue(row.LaneId, out var sender) && !sender.OutcomeTrusted)
        {
            _logger.LogInformation("不可信渠道的残留挂账按已送达销账：{LaneId} #{Id}", row.LaneId, row.Id);
            await _ledger.MarkSentAsync(row.Id, cancellationToken).ConfigureAwait(false);
            return;
        }

        await AttemptAsync(row, cancellationToken).ConfigureAwait(false);
    }

    private async Task AttemptAsync(DeliveryRecord row, CancellationToken cancellationToken)
    {
        // 同一条可能被「入队即发」和「周期补发」同时挑中：补发轮询能看见刚入队、还挂在通道上的
        // Pending 行，两次都发就会重复投递（文件尤甚，用户会收到两份）。按住行号串行化。
        if (!_inFlight.TryAdd(row.Id, 0))
        {
            return;
        }

        try
        {
            await AttemptCoreAsync(row, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _inFlight.TryRemove(row.Id, out _);
        }
    }

    private async Task AttemptCoreAsync(DeliveryRecord row, CancellationToken cancellationToken)
    {
        if (!_senders.TryGetValue(row.LaneId, out var sender))
        {
            await MarkFailureAsync(row, "没有该 Lane 的发送器", Backoff(row.Attempts + 1), cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        DeliveryPayload payload;
        try
        {
            payload = DeliveryPayload.FromJson(row.PayloadJson);
        }
        catch (Exception ex) when (ex is FormatException or System.Text.Json.JsonException)
        {
            // 负载坏了就别重试了：直接记成最后一次失败
            _logger.LogError(ex, "投递负载无法解析：{Id}（{Kind}）", row.Id, row.Kind);
            await _ledger.MarkFailedAsync(
                row.Id,
                "负载无法解析",
                _clock.GetUtcNow(),
                row.Attempts + MaxAttempts,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            switch (payload.Kind)
            {
                case DeliveryPayloadKind.Text:
                    await sender.SendTextAsync(payload.Target, payload.Text ?? string.Empty, cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case DeliveryPayloadKind.File:
                    await sender.SendFileAsync(
                        payload.Target,
                        payload.FilePath ?? throw new FormatException("文件负载缺少路径"),
                        payload.Caption,
                        cancellationToken).ConfigureAwait(false);
                    break;
                default:
                    throw new FormatException($"未知负载种类：{payload.Kind}");
            }

            await _ledger.MarkSentAsync(row.Id, cancellationToken).ConfigureAwait(false);
            await RecordAttemptAsync(row.LaneId, "ok", null, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (!sender.OutcomeTrusted)
        {
            // 不可信渠道：结果说不清。重发可能让用户收到两遍，所以按已送达销账，
            // 只留日志；要不要补是适配器自己的事。
            _logger.LogWarning(
                "不可信渠道发送结果不明（按已送达处理、不重投）：{LaneId} {Error}",
                row.LaneId,
                ex.Message);
            await _ledger.MarkSentAsync(row.Id, cancellationToken).ConfigureAwait(false);
            await RecordAttemptAsync(row.LaneId, "unknown", ex.Message, cancellationToken).ConfigureAwait(false);
        }
        catch (ChannelSendException ex)
        {
            await RecordAttemptAsync(row.LaneId, ResultOf(ex.Failure), ex.Message, cancellationToken).ConfigureAwait(false);
            await HandleSendFailureAsync(row, ex.Failure, ex.Message, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await RecordAttemptAsync(row.LaneId, "transient", ex.Message, cancellationToken).ConfigureAwait(false);
            await MarkFailureAsync(row, ex.Message, Backoff(row.Attempts + 1), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>失败类别 → 实测台账的结果串（与平台错误码解耦，只记通用类别）。</summary>
    private static string ResultOf(ChannelSendFailure failure) => failure switch
    {
        ChannelSendFailure.WindowClosed => "window_closed",
        ChannelSendFailure.SessionExpired => "session_expired",
        ChannelSendFailure.Permanent => "permanent",
        _ => "transient",
    };

    /// <summary>
    /// 记一条发送实测（距上次入站秒数 + 结果）。这是窗口期校准的数据源，失败只记日志，
    /// 绝不影响投递本身。
    /// </summary>
    private async Task RecordAttemptAsync(
        string laneId,
        string result,
        string? error,
        CancellationToken cancellationToken)
    {
        if (_attempts is null)
        {
            return;
        }

        try
        {
            var since = 0L;
            if (_lanes is not null
                && await _lanes.GetAsync(laneId, cancellationToken).ConfigureAwait(false) is { LastInboundAt: { } inbound })
            {
                since = Math.Max(0, (long)(_clock.GetUtcNow() - inbound).TotalSeconds);
            }

            await _attempts
                .AddAsync(new SendAttemptRecord(laneId, since, result, error), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "发送实测记账失败：{LaneId}", laneId);
        }
    }

    /// <summary>
    /// 按失败类别决定去向：窗口/会话 → 挂 2 小时等入站补发；
    /// 确定性失败 → 直接放弃（重试只会重复副作用）；其余按指数退避。
    /// </summary>
    private async Task HandleSendFailureAsync(
        DeliveryRecord row,
        ChannelSendFailure failure,
        string error,
        CancellationToken cancellationToken)
    {
        switch (failure)
        {
            case ChannelSendFailure.Permanent:
                _logger.LogWarning("投递永久失败，放弃（{LaneId}）：{Error}", row.LaneId, error);
                await _ledger
                    .MarkFailedAsync(row.Id, error, _clock.GetUtcNow(), row.Attempts + 1, cancellationToken)
                    .ConfigureAwait(false);
                return;

            case ChannelSendFailure.WindowClosed:
            case ChannelSendFailure.SessionExpired:
                var at = _clock.GetUtcNow() + WindowRetryDelay;
                _logger.LogInformation(
                    "投递暂缓（{Failure}），等 {LaneId} 下次入站补发：{Error}",
                    failure,
                    row.LaneId,
                    error);
                await _ledger.MarkFailedAsync(row.Id, error, at, MaxAttempts, cancellationToken)
                    .ConfigureAwait(false);
                return;

            default:
                await MarkFailureAsync(row, error, Backoff(row.Attempts + 1), cancellationToken)
                    .ConfigureAwait(false);
                return;
        }
    }

    private Task MarkFailureAsync(
        DeliveryRecord row,
        string error,
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        var next = _clock.GetUtcNow() + delay;
        _logger.LogWarning(
            "投递失败（第 {Attempt} 次，{LaneId}）：{Error}；下次 {Next:HH:mm:ss}",
            row.Attempts + 1,
            row.LaneId,
            error,
            next);
        return _ledger.MarkFailedAsync(row.Id, error, next, MaxAttempts, cancellationToken);
    }

    /// <summary>指数退避：5s、10s、20s…上限 5 分钟。</summary>
    private static TimeSpan Backoff(int attempt)
        => TimeSpan.FromSeconds(Math.Min(300, 5 * Math.Pow(2, Math.Max(0, attempt - 1))));
}
