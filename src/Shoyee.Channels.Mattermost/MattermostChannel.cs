using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shoyee.Contracts.Abstractions;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Options;

namespace Shoyee.Channels.Mattermost;

/// <summary>
/// 一个 Mattermost 账号的适配器（== 一条 Lane）：WebSocket 收 <c>posted</c>、REST 发帖/传文件/typing。
///
/// 会话粒度沿用 thread：频道里一个根帖 = 一条对话线索，回帖按 <c>root_id</c> 归位；
/// 私聊无 thread。回投地址的拼法见 <see cref="MattermostProtocol"/>。
/// 断线指数重连（2s → 60s），只在取消时返回。
/// </summary>
public sealed class MattermostChannel : IChannelAdapter, IChannelSender, ITypingChannel, IDisposable
{
    private readonly MattermostAccount _account;
    private readonly IInboundAttachmentStore _attachments;
    private readonly ILogger<MattermostChannel> _logger;
    private readonly MattermostClient _client;
    private readonly string _token;

    private volatile Me? _me;
    private volatile ChannelConnectionState _state = ChannelConnectionState.Disconnected;
    private volatile string? _lastError;

    /// <summary>创建适配器。</summary>
    /// <param name="options">账号与 Lane（token 只写环境变量名）。</param>
    /// <param name="attachments">入站附件落地。</param>
    /// <param name="logger">日志。</param>
    public MattermostChannel(
        [FromKeyedServices] MattermostChannelOptions options,
        IInboundAttachmentStore attachments,
        ILogger<MattermostChannel> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(attachments);
        ArgumentNullException.ThrowIfNull(logger);

        var account = options.Account;
        var laneId = options.LaneId;
        ArgumentNullException.ThrowIfNull(account);
        ArgumentException.ThrowIfNullOrWhiteSpace(laneId);

        var token = Environment.GetEnvironmentVariable(account.TokenEnv);
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException(
                $"Mattermost 配置了 {laneId}，但环境变量 {account.TokenEnv} 未设置（凭据不入库）。");
        }

        _account = account;
        LaneId = laneId;
        _attachments = attachments;
        _logger = logger;
        _token = token;
        _client = new MattermostClient(account.BaseUrl, token);
    }

    /// <inheritdoc />
    public string LaneId { get; }

    /// <inheritdoc />
    public ChannelConnectionState State => _state;

    /// <inheritdoc />
    public string? LastError => _lastError;

    /// <inheritdoc />
    public async Task RunAsync(IInboundSink sink, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sink);

        var backoffSeconds = 2;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                _state = ChannelConnectionState.Connecting;
                var me = await _client.GetMeAsync(cancellationToken).ConfigureAwait(false);
                _me = new Me(me.Id, me.Username);
                _state = ChannelConnectionState.Connected;
                _lastError = null;
                backoffSeconds = 2;
                _logger.LogInformation(
                    "Mattermost {LaneId} 已登录：@{Username}（{UserId}）",
                    LaneId,
                    me.Username,
                    me.Id);

                await ListenAsync(sink, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _state = ChannelConnectionState.Disconnected;
                _lastError = ex.Message;
                _logger.LogWarning(ex, "Mattermost {LaneId} 连接异常，{Seconds}s 后重连", LaneId, backoffSeconds);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(backoffSeconds), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                backoffSeconds = Math.Min(backoffSeconds * 2, 60);
            }
        }

        _state = ChannelConnectionState.Disconnected;
        _logger.LogInformation("Mattermost {LaneId} 已停止", LaneId);
    }

    /// <inheritdoc />
    public async Task SendTextAsync(string target, string text, CancellationToken cancellationToken = default)
    {
        if (text.Length == 0)
        {
            return; // Mattermost 不接受空正文帖
        }

        var (channelId, threadId) = MattermostProtocol.ParseTarget(target);
        foreach (var chunk in MattermostProtocol.SplitPost(text))
        {
            await _client.CreatePostAsync(channelId, chunk, threadId, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task SendFileAsync(
        string target,
        string filePath,
        string? caption,
        CancellationToken cancellationToken = default)
    {
        var (channelId, threadId) = MattermostProtocol.ParseTarget(target);
        var data = await File.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);
        var fileId = await _client
            .UploadFileAsync(channelId, Path.GetFileName(filePath), data, cancellationToken)
            .ConfigureAwait(false);
        await _client
            .CreatePostAsync(channelId, caption ?? string.Empty, threadId, [fileId], cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task SendTypingAsync(string target, CancellationToken cancellationToken = default)
    {
        var me = _me;
        if (me is null)
        {
            return Task.CompletedTask; // 还没连上，typing 没意义
        }

        var (channelId, _) = MattermostProtocol.ParseTarget(target);
        return _client.SendTypingAsync(me.Id, channelId, cancellationToken);
    }

    /// <inheritdoc />
    public Task StopTypingAsync(string target, CancellationToken cancellationToken = default)
        => Task.CompletedTask; // Mattermost typing 事件约 5s 自动过期，无需显式停

    private async Task ListenAsync(IInboundSink sink, CancellationToken cancellationToken)
    {
        using var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Origin", _account.BaseUrl);
        await socket.ConnectAsync(_client.WebSocketUri, cancellationToken).ConfigureAwait(false);

        var token = _token;
        await SendJsonAsync(
            socket,
            $"{{\"seq\":1,\"action\":\"authentication_challenge\",\"data\":{{\"token\":\"{token}\"}}}}",
            cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Mattermost {LaneId} WebSocket 已连接：{Uri}", LaneId, _client.WebSocketUri);

        var buffer = new byte[16 * 1024];
        using var message = new MemoryStream();
        while (!cancellationToken.IsCancellationRequested)
        {
            message.SetLength(0);
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    throw new InvalidOperationException("Mattermost WebSocket 被关闭");
                }

                message.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            var json = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
            await HandleEventAsync(json, sink).ConfigureAwait(false);
        }
    }

    private async Task HandleEventAsync(string json, IInboundSink sink)
    {
        var me = _me;
        if (me is null)
        {
            return;
        }

        MattermostPost? post;
        try
        {
            post = MattermostProtocol.ParsePostedEvent(json, me.Username, me.Id);
        }
        catch (JsonException)
        {
            return; // 非 posted 的其它事件形状，忽略
        }

        if (post is null
            || !MattermostProtocol.ShouldHandle(post, me.Id, _account.AllowedUsers, _account.RequireMention))
        {
            return;
        }

        var attachments = await DownloadAttachmentsAsync(post).ConfigureAwait(false);
        var text = MattermostProtocol.StripMention(post.Message, me.Username).Trim();
        if (text.Length == 0 && attachments.Count == 0)
        {
            return;
        }

        var message = new InboundMessage(
            LaneId,
            post.Id,
            MattermostProtocol.BuildTarget(post),
            post.UserId,
            null,
            text,
            post.ChannelType == "D" || post.Mentioned,
            DateTimeOffset.UtcNow,
            attachments.Count > 0 ? attachments : null);

        _logger.LogInformation(
            "Mattermost {LaneId} 收到消息：{Target} ← {Sender}{Attachments}",
            LaneId,
            message.Target,
            post.UserId,
            attachments.Count > 0 ? $"（{attachments.Count} 个附件）" : string.Empty);

        await sink.OnInboundAsync(message).ConfigureAwait(false);
    }

    /// <summary>逐个下载附件落盘；单个失败只记日志，不丢整条消息。</summary>
    private async Task<List<InboundAttachment>> DownloadAttachmentsAsync(MattermostPost post)
    {
        var attachments = new List<InboundAttachment>();
        foreach (var fileId in post.FileIds)
        {
            try
            {
                var (name, contentType) = await _client.GetFileInfoAsync(fileId).ConfigureAwait(false);
                var data = await _client.DownloadFileAsync(fileId).ConfigureAwait(false);
                attachments.Add(await _attachments.SaveAsync(
                    name.Length > 0 ? name : fileId,
                    contentType.Length > 0 ? contentType : null,
                    data).ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Mattermost 附件下载失败（跳过）：{FileId}", fileId);
            }
        }

        return attachments;
    }

    private static async Task SendJsonAsync(ClientWebSocket socket, string json, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose() => _client.Dispose();

    private sealed record Me(string Id, string Username);
}
