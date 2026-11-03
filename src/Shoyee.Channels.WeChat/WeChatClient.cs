using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Shoyee.Channels.WeChat;

/// <summary>发送结果分类。</summary>
internal enum WeChatSendStatus
{
    /// <summary>成功。</summary>
    Ok,

    /// <summary>会话过期（-14）：按平台语义可降级 tokenless 重试一次。</summary>
    SessionExpired,

    /// <summary>「用户来消息的窗口期」外（-2 prepare failed）：主动发送被拒。</summary>
    WindowClosed,

    /// <summary>其它失败（含结果不确定）。</summary>
    Failed,
}

/// <summary>发送结果。</summary>
/// <param name="Status">结果分类。</param>
/// <param name="MessageId">成功时的服务端消息 id（不一定有）。</param>
/// <param name="Error">失败描述。</param>
/// <param name="ContextToken">响应里刷新的续接令牌（有则覆盖缓存）。</param>
internal sealed record WeChatSendResult(WeChatSendStatus Status, string? MessageId, string? Error, string? ContextToken);

/// <summary>
/// 腾讯 iLink Bot API 薄客户端：长轮询 <c>getupdates</c>、<c>sendmessage</c>、
/// <c>getconfig</c> 取 typing 票据、<c>sendtyping</c>，以及媒体收发
/// （<c>getuploadurl</c> → AES-128-ECB 加密上传 CDN → 媒体 item；下载反向解密）。
///
/// 协议细节来自 Hermes 适配器的只读调研 + Secretary 的线上实测，供应商方言全部留在本项目。
/// </summary>
internal sealed class WeChatClient : IDisposable
{
    /// <summary>默认微信 CDN 接入点。</summary>
    public const string DefaultCdnBaseUrl = "https://novac2c.cdn.weixin.qq.com/c2c";

    private const string AppId = "bot";
    private const string ChannelVersion = "2.2.0";
    private const int AppClientVersion = (2 << 16) | (2 << 8) | 0;

    private static readonly TimeSpan LongPollTimeout = TimeSpan.FromSeconds(40);
    private static readonly TimeSpan ApiTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan CdnTimeout = TimeSpan.FromSeconds(120);

    private readonly HttpClient _http;
    private readonly string _token;
    private readonly string _cdnBaseUrl;

    /// <summary>创建客户端。</summary>
    /// <param name="http">HTTP 客户端（BaseAddress 会被设置）。</param>
    /// <param name="baseUrl">iLink 接入点。</param>
    /// <param name="token">bot token。</param>
    /// <param name="cdnBaseUrl">媒体 CDN 接入点（可空 = 官方默认）。</param>
    public WeChatClient(HttpClient http, string baseUrl, string token, string? cdnBaseUrl = null)
    {
        _http = http;
        _http.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        _http.Timeout = Timeout.InfiniteTimeSpan; // 每个请求自带超时（长轮询 40s，不能吃 HttpClient 的默认 100s）
        _token = token;
        _cdnBaseUrl = (cdnBaseUrl ?? DefaultCdnBaseUrl).TrimEnd('/');
    }

    /// <summary>长轮询拉取新消息。</summary>
    /// <param name="syncBuf">上次的游标。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<(IReadOnlyList<WeChatInbound> Messages, string NextBuf)> GetUpdatesAsync(
        string syncBuf,
        CancellationToken cancellationToken = default)
    {
        using var doc = await PostAsync(
            "ilink/bot/getupdates",
            new Dictionary<string, object?> { ["get_updates_buf"] = syncBuf },
            LongPollTimeout,
            cancellationToken).ConfigureAwait(false);
        var root = doc.RootElement;

        if (IsSessionExpired(root))
        {
            throw new InvalidOperationException("iLink getupdates 会话过期（-14）");
        }

        var messages = new List<WeChatInbound>();
        if (root.TryGetProperty("msgs", out var msgs) && msgs.ValueKind == JsonValueKind.Array)
        {
            foreach (var msg in msgs.EnumerateArray())
            {
                if (WeChatProtocol.ParseMessage(msg) is { } parsed)
                {
                    messages.Add(parsed);
                }
            }
        }

        var nextBuf = root.TryGetProperty("get_updates_buf", out var buf) ? buf.GetString() ?? syncBuf : syncBuf;
        return (messages, nextBuf);
    }

    /// <summary>发文本消息；<paramref name="contextToken"/> 为空 = tokenless 降级发送。</summary>
    public Task<WeChatSendResult> SendTextAsync(
        string to,
        string text,
        string? contextToken,
        CancellationToken cancellationToken = default)
        => SendItemsAsync(
            to,
            [new Dictionary<string, object> { ["type"] = 1, ["text_item"] = new Dictionary<string, object> { ["text"] = text } }],
            contextToken,
            cancellationToken);

    /// <summary>
    /// 发文件：<c>getuploadurl</c> 拿上传地址 → AES-128-ECB 加密整文件 → POST 密文上 CDN
    /// （响应头 <c>x-encrypted-param</c>）→ <c>sendmessage</c> 发媒体 item。
    /// 图片/视频走各自 item，其余（含音频）一律文件附件。
    /// </summary>
    public async Task<WeChatSendResult> SendFileAsync(
        string to,
        string filePath,
        string? contextToken,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var plaintext = await File.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);
            var (uploadType, kind) = WeChatMedia.ClassifyByFileName(filePath);
            var fileName = Path.GetFileName(filePath);
            var filekey = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
            var aesKey = RandomNumberGenerator.GetBytes(16);
            var rawsize = plaintext.LongLength;
            var rawFileMd5 = Convert.ToHexString(MD5.HashData(plaintext)).ToLowerInvariant();
            var fileSize = ((rawsize + 16) / 16) * 16; // 16 字节对齐的密文长度（PKCS7）

            using var uploadDoc = await PostAsync(
                "ilink/bot/getuploadurl",
                new Dictionary<string, object?>
                {
                    ["filekey"] = filekey,
                    ["media_type"] = uploadType,
                    ["to_user_id"] = to,
                    ["rawsize"] = rawsize,
                    ["rawfilemd5"] = rawFileMd5,
                    ["filesize"] = fileSize,
                    ["no_need_thumb"] = true,
                    ["aeskey"] = Convert.ToHexString(aesKey).ToLowerInvariant(),
                },
                ApiTimeout,
                cancellationToken).ConfigureAwait(false);
            var uploadRoot = uploadDoc.RootElement;
            var uploadUrl = uploadRoot.TryGetProperty("upload_full_url", out var full) ? full.GetString() : null;
            if (string.IsNullOrEmpty(uploadUrl))
            {
                // 退路：upload_param 拼 CDN 地址（必须 POST——PUT 在 CDN 上 404）
                var uploadParam = uploadRoot.TryGetProperty("upload_param", out var p) ? p.GetString() : null;
                if (string.IsNullOrEmpty(uploadParam))
                {
                    return new WeChatSendResult(
                        WeChatSendStatus.Failed,
                        null,
                        "getuploadurl 既无 upload_full_url 也无 upload_param",
                        null);
                }

                uploadUrl =
                    $"{_cdnBaseUrl}/upload?encrypted_query_param={Uri.EscapeDataString(uploadParam)}&filekey={Uri.EscapeDataString(filekey)}";
            }

            var ciphertext = WeChatMedia.Encrypt(plaintext, aesKey);
            var encryptedQuery = await UploadCiphertextAsync(uploadUrl, ciphertext, cancellationToken).ConfigureAwait(false);

            var item = WeChatMedia.BuildOutboundItem(
                kind,
                encryptedQuery,
                WeChatMedia.EncodeAesKeyForApi(aesKey),
                ciphertext.LongLength,
                rawsize,
                fileName,
                rawFileMd5);
            return await SendItemsAsync(to, [item], contextToken, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 上传链路失败 = 消息本体还没提交，可以确定没发出去
            return new WeChatSendResult(WeChatSendStatus.Failed, null, ex.Message, null);
        }
    }

    /// <summary>下载并解密一个入站媒体 item（<c>full_url</c> 走 CDN 白名单防 SSRF）。</summary>
    public async Task<byte[]> DownloadMediaAsync(WeChatMediaRef media, CancellationToken cancellationToken = default)
    {
        var url = !string.IsNullOrEmpty(media.EncryptedQueryParam)
            ? $"{_cdnBaseUrl}/download?encrypted_query_param={Uri.EscapeDataString(media.EncryptedQueryParam)}"
            : media.FullUrl ?? throw new InvalidOperationException("媒体 item 既无 encrypt_query_param 也无 full_url");
        if (!WeChatMedia.IsAllowedCdnUrl(url))
        {
            throw new InvalidOperationException($"媒体下载地址不在微信 CDN 白名单：{url}");
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CdnTimeout);
        using var response = await _http.GetAsync(url, cts.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var raw = await response.Content.ReadAsByteArrayAsync(cts.Token).ConfigureAwait(false);

        var key = WeChatMedia.ResolveAesKey(media);
        return key is null ? raw : WeChatMedia.Decrypt(raw, key);
    }

    /// <summary>取 typing 票据（<c>getconfig</c>，约 600s 有效，调用方缓存）。</summary>
    public async Task<string?> GetTypingTicketAsync(
        string userId,
        string? contextToken,
        CancellationToken cancellationToken = default)
    {
        var payload = new Dictionary<string, object?> { ["ilink_user_id"] = userId };
        if (!string.IsNullOrEmpty(contextToken))
        {
            payload["context_token"] = contextToken;
        }

        try
        {
            using var doc = await PostAsync("ilink/bot/getconfig", payload, ApiTimeout, cancellationToken)
                .ConfigureAwait(false);
            var ticket = doc.RootElement.TryGetProperty("typing_ticket", out var t) ? t.GetString() : null;
            return string.IsNullOrEmpty(ticket) ? null : ticket;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>typing 信号（<c>sendtyping</c>，status 1=开始 2=停止；停不掉会一直转圈，必须成对）。</summary>
    public async Task SetTypingAsync(
        string userId,
        string ticket,
        bool start,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var _ = await PostAsync(
                "ilink/bot/sendtyping",
                new Dictionary<string, object?>
                {
                    ["ilink_user_id"] = userId,
                    ["typing_ticket"] = ticket,
                    ["status"] = start ? 1 : 2,
                },
                ApiTimeout,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // typing 是尽力而为的信号，失败不影响主流程
        }
    }

    /// <summary>POST 密文到 CDN，取响应头 <c>x-encrypted-param</c>（= encrypted_query_param）。</summary>
    private async Task<string> UploadCiphertextAsync(
        string uploadUrl,
        byte[] ciphertext,
        CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CdnTimeout);
        using var request = new HttpRequestMessage(HttpMethod.Post, uploadUrl)
        {
            Content = new ByteArrayContent(ciphertext),
        };
        request.Content.Headers.TryAddWithoutValidation("Content-Type", "application/octet-stream");
        using var response = await _http.SendAsync(request, cts.Token).ConfigureAwait(false);
        var bodyPreview = response.IsSuccessStatusCode
            ? string.Empty
            : await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return response.Headers.TryGetValues("x-encrypted-param", out var values)
            ? values.FirstOrDefault() ?? throw new InvalidOperationException("CDN 上传响应缺 x-encrypted-param 头")
            : throw new InvalidOperationException(
                $"CDN 上传响应缺 x-encrypted-param 头：{bodyPreview[..Math.Min(200, bodyPreview.Length)]}");
    }

    /// <summary>发一组 item（sendmessage）；响应判定见 <see cref="ParseSendResult"/>。</summary>
    private async Task<WeChatSendResult> SendItemsAsync(
        string to,
        IReadOnlyList<Dictionary<string, object>> items,
        string? contextToken,
        CancellationToken cancellationToken)
    {
        var message = new Dictionary<string, object?>
        {
            ["from_user_id"] = string.Empty,
            ["to_user_id"] = to,
            ["client_id"] = "shoyee-" + Guid.NewGuid().ToString("N"),
            ["message_type"] = 2,
            ["message_state"] = 2,
            ["item_list"] = items,
        };
        if (!string.IsNullOrEmpty(contextToken))
        {
            message["context_token"] = contextToken;
        }

        try
        {
            using var doc = await PostAsync(
                "ilink/bot/sendmessage",
                new Dictionary<string, object?> { ["msg"] = message },
                ApiTimeout,
                cancellationToken).ConfigureAwait(false);
            return ParseSendResult(doc.RootElement);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 网络异常 = 结果不确定；调用方按「不可信渠道」处理，不自动重发
            return new WeChatSendResult(WeChatSendStatus.Failed, null, ex.Message, null);
        }
    }

    /// <summary>
    /// <c>sendmessage</c> 响应判定：<c>ret</c>/<c>errcode</c> 缺省或 0 = 成功——
    /// <c>message_id</c> 不一定在根上、不一定有，死等它会把成功误判成失败 → 重发 = 重复刷屏。
    /// <c>-2</c> 分两种：prepare failed = 窗口外；unknown error = 伪过期。
    /// </summary>
    internal static WeChatSendResult ParseSendResult(JsonElement root)
    {
        var ret = TryGetInt(root, "ret") ?? TryGetInt(root, "errcode");
        var error = root.TryGetProperty("errmsg", out var e1)
            ? e1.GetString()
            : root.TryGetProperty("msg", out var e2) && e2.ValueKind == JsonValueKind.String
                ? e2.GetString()
                : null;

        if (ret is null or 0)
        {
            var ctx = root.TryGetProperty("context_token", out var c) ? c.GetString() : null;
            return new WeChatSendResult(WeChatSendStatus.Ok, FindMessageId(root), error, ctx);
        }

        if (ret == -14)
        {
            return new WeChatSendResult(WeChatSendStatus.SessionExpired, null, "session expired (-14)", null);
        }

        if (ret == -2)
        {
            return error?.Contains("prepare", StringComparison.OrdinalIgnoreCase) == true
                ? new WeChatSendResult(WeChatSendStatus.WindowClosed, null, error, null)
                : new WeChatSendResult(WeChatSendStatus.SessionExpired, null, error ?? "stale session (-2)", null);
        }

        return new WeChatSendResult(WeChatSendStatus.Failed, null, error ?? $"ret={ret}", null);
    }

    private async Task<JsonDocument> PostAsync(
        string endpoint,
        Dictionary<string, object?> payload,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        payload["base_info"] = new Dictionary<string, object> { ["channel_version"] = ChannelVersion };
        var body = JsonSerializer.Serialize(payload);

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("AuthorizationType", "ilink_bot_token");
        request.Headers.TryAddWithoutValidation("X-WECHAT-UIN", RandomUin());
        request.Headers.TryAddWithoutValidation("iLink-App-Id", AppId);
        request.Headers.TryAddWithoutValidation("iLink-App-ClientVersion", AppClientVersion.ToString());
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _token);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        using var response = await _http.SendAsync(request, timeoutCts.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);
        return JsonDocument.Parse(json);
    }

    private static bool IsSessionExpired(JsonElement root)
        => TryGetInt(root, "ret") == -14 || TryGetInt(root, "errcode") == -14;

    /// <summary><c>message_id</c> 可能在根上，也可能嵌在 <c>msg</c>/<c>data</c> 对象里。</summary>
    private static string? FindMessageId(JsonElement root)
    {
        foreach (var candidate in new[] { root, GetObject(root, "msg"), GetObject(root, "data") })
        {
            if (candidate is { } el
                && el.TryGetProperty("message_id", out var mid)
                && mid.ValueKind == JsonValueKind.String
                && mid.GetString() is { Length: > 0 } id)
            {
                return id;
            }
        }

        return null;
    }

    private static JsonElement? GetObject(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object ? value : null;

    private static int? TryGetInt(JsonElement root, string name)
        => root.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var i)
                ? i
                : null;

    /// <inheritdoc />
    public void Dispose() => _http.Dispose();

    private static string RandomUin()
    {
        var uin = RandomNumberGenerator.GetInt32(int.MaxValue).ToString();
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(uin));
    }
}
