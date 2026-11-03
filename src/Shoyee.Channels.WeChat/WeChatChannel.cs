using Microsoft.Extensions.Logging;
using Shoyee.Contracts.Abstractions;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Options;

namespace Shoyee.Channels.WeChat;

/// <summary>
/// 一个微信账号的适配器（== 一条 Lane，只做工作模式）：
/// 长轮询 <c>getupdates</c> 收消息、<c>sendmessage</c> 发消息、<c>sendtyping</c> 显示输入中。
///
/// 平台自己的事全在这里：窗口期判定、逐条限速、context_token、typing 票据、媒体加密。
/// **发送结果不可信**（超时可能已投递、平台会自己重发），所以声明 <see cref="OutcomeTrusted"/> = false：
/// 台账只尝试一次、不二次投递；要不要补发由本适配器决定（当前策略：宁可漏发，不重复刷屏）。
/// </summary>
public sealed class WeChatChannel : IChannelAdapter, IChannelSender, ITypingChannel, IDisposable
{
    private static readonly TimeSpan ChunkDelay = TimeSpan.FromMilliseconds(800);
    private static readonly TimeSpan TypingTicketTtl = TimeSpan.FromSeconds(590);
    private static readonly TimeSpan DedupeTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan SessionExpiredDelay = TimeSpan.FromMinutes(10);

    /// <summary>全局出站最小间隔：突发发送会被 iLink 限速打回 prepare failed，匀速发。</summary>
    private static readonly TimeSpan SendGap = TimeSpan.FromMilliseconds(1200);

    private readonly WeChatAccount _account;
    private readonly IInboundAttachmentStore _attachments;
    private readonly ILogger<WeChatChannel> _logger;
    private readonly WeChatClient _client;
    private readonly WeChatStateStore _state;
    private readonly Dictionary<string, DateTimeOffset> _seen = new(StringComparer.Ordinal);
    private readonly Lock _seenGate = new();
    private readonly SemaphoreSlim _sendGate = new(1, 1);

    private DateTimeOffset _lastSend = DateTimeOffset.MinValue;
    private volatile ChannelConnectionState _stateValue = ChannelConnectionState.Disconnected;
    private volatile string? _lastError;

    /// <summary>创建适配器。</summary>
    /// <param name="account">账号配置（token 只写环境变量名）。</param>
    /// <param name="laneId">该账号对应的 Lane id。</param>
    /// <param name="workspaceRoot">工作区根目录（状态文件落 <c>wechat/state.json</c>）。</param>
    /// <param name="attachments">入站附件落地。</param>
    /// <param name="logger">日志。</param>
    public WeChatChannel(
        WeChatAccount account,
        string laneId,
        string workspaceRoot,
        IInboundAttachmentStore attachments,
        ILogger<WeChatChannel> logger)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentException.ThrowIfNullOrWhiteSpace(laneId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentNullException.ThrowIfNull(attachments);
        ArgumentNullException.ThrowIfNull(logger);

        var token = Environment.GetEnvironmentVariable(account.TokenEnv);
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException(
                $"微信配置了 {laneId}，但环境变量 {account.TokenEnv} 未设置（凭据不入库）。");
        }

        _account = account;
        LaneId = laneId;
        _attachments = attachments;
        _logger = logger;
        _client = new WeChatClient(new HttpClient(new SocketsHttpHandler()), account.BaseUrl, token, account.CdnBaseUrl);
        _state = new WeChatStateStore(workspaceRoot);
    }

    /// <summary>单条消息字符上限。</summary>
    public static int MaxMessageLength => WeChatProtocol.MaxMessageLength;

    /// <inheritdoc />
    public string LaneId { get; }

    /// <inheritdoc />
    public ChannelConnectionState State => _stateValue;

    /// <inheritdoc />
    public string? LastError => _lastError;

    /// <inheritdoc />
    public bool OutcomeTrusted => false;

    /// <inheritdoc />
    public async Task RunAsync(IInboundSink sink, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sink);

        var backoffSeconds = 2;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                _stateValue = ChannelConnectionState.Connecting;
                var (messages, nextBuf) = await _client.GetUpdatesAsync(_state.SyncBuf, cancellationToken)
                    .ConfigureAwait(false);

                _stateValue = ChannelConnectionState.Connected;
                _lastError = null;
                backoffSeconds = 2;

                if (nextBuf != _state.SyncBuf)
                {
                    _state.SyncBuf = nextBuf;
                }

                foreach (var message in messages)
                {
                    await HandleMessageAsync(message, sink).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (ex.Message.Contains("会话过期", StringComparison.Ordinal))
            {
                // -14：长歇，不参与指数退避
                _stateValue = ChannelConnectionState.Disconnected;
                _lastError = ex.Message;
                _logger.LogWarning("微信会话过期（-14），{Minutes} 分钟后重试", SessionExpiredDelay.TotalMinutes);
                if (!await DelayAsync(SessionExpiredDelay, cancellationToken).ConfigureAwait(false))
                {
                    break;
                }
            }
            catch (Exception ex)
            {
                _stateValue = ChannelConnectionState.Disconnected;
                _lastError = ex.Message;
                _logger.LogWarning(ex, "微信轮询异常，{Seconds}s 后重试", backoffSeconds);
                if (!await DelayAsync(TimeSpan.FromSeconds(backoffSeconds), cancellationToken).ConfigureAwait(false))
                {
                    break;
                }

                backoffSeconds = Math.Min(backoffSeconds * 2, 60);
            }
        }

        _stateValue = ChannelConnectionState.Disconnected;
        _logger.LogInformation("微信 {LaneId} 已停止", LaneId);
    }

    /// <inheritdoc />
    public Task SendTextAsync(string target, string text, CancellationToken cancellationToken = default)
        => SendCoreAsync(target, text, filePath: null, cancellationToken);

    /// <inheritdoc />
    public Task SendFileAsync(
        string target,
        string filePath,
        string? caption,
        CancellationToken cancellationToken = default)
        => SendCoreAsync(target, caption ?? string.Empty, filePath, cancellationToken);

    /// <inheritdoc />
    public async Task SendTypingAsync(string target, CancellationToken cancellationToken = default)
    {
        var (peer, _) = WeChatProtocol.ParseTarget(target);
        var ticket = await EnsureTypingTicketAsync(peer, cancellationToken).ConfigureAwait(false);
        if (ticket is not null)
        {
            await _client.SetTypingAsync(peer, ticket, start: true, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task StopTypingAsync(string target, CancellationToken cancellationToken = default)
    {
        var (peer, _) = WeChatProtocol.ParseTarget(target);
        var ticket = _state.GetTypingTicket(peer);
        if (ticket is not null)
        {
            await _client.SetTypingAsync(peer, ticket, start: false, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 发送核心：正文逐段、附件逐个；每段前加持全局最小间隔。
    /// 失败按平台语义翻译成 <see cref="ChannelSendException"/>（台账据此决定是否重试）。
    /// </summary>
    private async Task SendCoreAsync(string target, string text, string? filePath, CancellationToken cancellationToken)
    {
        var (peer, _) = WeChatProtocol.ParseTarget(target);

        if (text.Length > 0)
        {
            foreach (var chunk in WeChatProtocol.SplitText(text))
            {
                var result = await SendPacedAsync(peer, chunk, _state.GetContextToken(peer), cancellationToken)
                    .ConfigureAwait(false);
                if (result.Status == WeChatSendStatus.SessionExpired)
                {
                    // 平台语义：-14 时 tokenless 降级重试一次
                    result = await SendPacedAsync(peer, chunk, null, cancellationToken).ConfigureAwait(false);
                }

                HandleSendResult(peer, result);
                await Task.Delay(ChunkDelay, cancellationToken).ConfigureAwait(false);
            }
        }

        if (filePath is not null)
        {
            var result = await SendFilePacedAsync(peer, filePath, _state.GetContextToken(peer), cancellationToken)
                .ConfigureAwait(false);
            if (result.Status == WeChatSendStatus.SessionExpired)
            {
                result = await SendFilePacedAsync(peer, filePath, null, cancellationToken).ConfigureAwait(false);
            }

            HandleSendResult(peer, result);
        }
    }

    private void HandleSendResult(string peer, WeChatSendResult result)
    {
        switch (result.Status)
        {
            case WeChatSendStatus.Ok:
                if (!string.IsNullOrEmpty(result.ContextToken))
                {
                    _state.SetContextToken(peer, result.ContextToken);
                }

                return;

            case WeChatSendStatus.WindowClosed:
                _logger.LogWarning("微信发送被拒（用户消息窗口外）：{Peer}", peer);
                throw new ChannelSendException(
                    ChannelSendFailure.WindowClosed,
                    "微信用户消息窗口外（ret:-2 prepare failed）");

            case WeChatSendStatus.SessionExpired:
                throw new ChannelSendException(
                    ChannelSendFailure.SessionExpired,
                    result.Error ?? "微信会话过期（-14）");

            default:
                // 结果不确定：可能已投递也可能没有。本适配器声明 OutcomeTrusted=false，
                // 台账不会重投；这里按「已送达」语义吞掉，只留日志。
                _logger.LogWarning("微信发送结果不确定（按已送达处理、不重发）：{Peer} {Error}", peer, result.Error);
                return;
        }
    }

    private async Task<WeChatSendResult> SendPacedAsync(
        string peer,
        string text,
        string? contextToken,
        CancellationToken cancellationToken)
    {
        await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await WaitSendGapAsync(cancellationToken).ConfigureAwait(false);
            return await _client.SendTextAsync(peer, text, contextToken, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sendGate.Release();
        }
    }

    private async Task<WeChatSendResult> SendFilePacedAsync(
        string peer,
        string filePath,
        string? contextToken,
        CancellationToken cancellationToken)
    {
        await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await WaitSendGapAsync(cancellationToken).ConfigureAwait(false);
            return await _client.SendFileAsync(peer, filePath, contextToken, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sendGate.Release();
        }
    }

    private async Task WaitSendGapAsync(CancellationToken cancellationToken)
    {
        var wait = _lastSend + SendGap - DateTimeOffset.UtcNow;
        if (wait > TimeSpan.Zero)
        {
            await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
        }

        _lastSend = DateTimeOffset.UtcNow;
    }

    private async Task<string?> EnsureTypingTicketAsync(string peer, CancellationToken cancellationToken)
    {
        var ticket = _state.GetTypingTicket(peer);
        if (ticket is not null)
        {
            return ticket;
        }

        ticket = await _client
            .GetTypingTicketAsync(peer, _state.GetContextToken(peer), cancellationToken)
            .ConfigureAwait(false);
        if (ticket is not null)
        {
            _state.SetTypingTicket(peer, ticket, TypingTicketTtl);
        }

        return ticket;
    }

    private async Task HandleMessageAsync(WeChatInbound message, IInboundSink sink)
    {
        if (!string.IsNullOrEmpty(_account.AccountId) && message.FromUserId == _account.AccountId)
        {
            return; // 自发消息回声
        }

        if (_account.AllowedUsers is { Count: > 0 } allowed && !allowed.Contains(message.FromUserId))
        {
            return;
        }

        if (!MarkSeen(message.MessageId))
        {
            return; // 窗口内重复投递
        }

        if (!string.IsNullOrEmpty(message.ContextToken))
        {
            _state.SetContextToken(message.FromUserId, message.ContextToken);
        }

        var attachments = await DownloadAttachmentsAsync(message).ConfigureAwait(false);

        if (message.Text.Length == 0 && attachments.Count == 0)
        {
            return;
        }

        // 内容指纹二级去重：上游会用新 message_id 重发同内容；只对非空文本做
        if (message.Text.Length > 0
            && !MarkSeen(WeChatProtocol.ContentDedupeKey(message.FromUserId, message.Text)))
        {
            return;
        }

        var inbound = new InboundMessage(
            LaneId,
            message.MessageId,
            WeChatProtocol.BuildTarget(message),
            message.FromUserId,
            null,
            message.Text,
            AddressedToBot: true,
            DateTimeOffset.UtcNow,
            attachments.Count > 0 ? attachments : null);

        _logger.LogInformation(
            "微信 {LaneId} 收到消息：{Target} ← {Sender}{Attachments}",
            LaneId,
            inbound.Target,
            message.FromUserId,
            attachments.Count > 0 ? $"（{attachments.Count} 个附件）" : string.Empty);

        await sink.OnInboundAsync(inbound).ConfigureAwait(false);
    }

    /// <summary>逐个下载媒体 item 落盘；失败跳过（不影响正文）。</summary>
    private async Task<List<InboundAttachment>> DownloadAttachmentsAsync(WeChatInbound message)
    {
        var attachments = new List<InboundAttachment>();
        foreach (var media in message.Media ?? (IReadOnlyList<WeChatMediaRef>)Array.Empty<WeChatMediaRef>())
        {
            try
            {
                var data = await _client.DownloadMediaAsync(media, CancellationToken.None).ConfigureAwait(false);
                var fileName = media.FileName.Length > 0 ? media.FileName : DefaultFileName(media.Kind);
                attachments.Add(await _attachments.SaveAsync(fileName, null, data).ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "微信附件下载失败（跳过）：{Kind} {FileName}", media.Kind, media.FileName);
            }
        }

        return attachments;
    }

    private static string DefaultFileName(WeChatMediaKind kind) => kind switch
    {
        WeChatMediaKind.Image => "image.jpg",
        WeChatMediaKind.Voice => "voice.silk",
        WeChatMediaKind.Video => "video.mp4",
        _ => "document.bin",
    };

    private bool MarkSeen(string key)
    {
        lock (_seenGate)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var stale in _seen.Where(kv => now - kv.Value > DedupeTtl).Select(kv => kv.Key).ToList())
            {
                _seen.Remove(stale);
            }

            if (!_seen.TryAdd(key, now))
            {
                return false;
            }

            return true;
        }
    }

    private static async Task<bool> DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _client.Dispose();
        _sendGate.Dispose();
    }
}
