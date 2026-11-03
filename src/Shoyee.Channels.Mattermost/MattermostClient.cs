using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Shoyee.Channels.Mattermost;

/// <summary>
/// Mattermost REST API v4 薄客户端（不引第三方 SDK）：自检、发帖、传/取附件、typing。
///
/// 两个 HttpClient 共用连接池但超时不同：API 15s；文件传/取 120s
/// （线上实锤——批量发图共用 15s 超时全军覆没）。
/// </summary>
internal sealed class MattermostClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly HttpClient _fileHttp;

    /// <summary>创建客户端（token 走 Bearer 头）。</summary>
    /// <param name="serverUrl">服务器地址（如 <c>https://mm.example.com</c>）。</param>
    /// <param name="token">bot token。</param>
    public MattermostClient(string serverUrl, string token)
    {
        var handler = new SocketsHttpHandler();
        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri(serverUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(15),
        };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        _fileHttp = new HttpClient(handler, disposeHandler: false)
        {
            BaseAddress = _http.BaseAddress,
            Timeout = TimeSpan.FromSeconds(120),
        };
        _fileHttp.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    /// <summary>WebSocket 地址（<c>https → wss</c>）。</summary>
    public Uri WebSocketUri
    {
        get
        {
            var baseUrl = _http.BaseAddress!.ToString().TrimEnd('/');
            return new Uri(baseUrl.Replace("https://", "wss://").Replace("http://", "ws://") + "/api/v4/websocket");
        }
    }

    /// <summary>鉴权自检，返回 bot 的 (id, username)。</summary>
    public async Task<(string Id, string Username)> GetMeAsync(CancellationToken cancellationToken = default)
    {
        using var doc = await _http.GetFromJsonAsync<JsonDocument>("api/v4/users/me", cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Mattermost /users/me 无响应");
        var root = doc.RootElement;
        return (
            root.GetProperty("id").GetString() ?? string.Empty,
            root.GetProperty("username").GetString() ?? string.Empty);
    }

    /// <summary>发帖（<c>root_id</c> 非空 = 进 thread；<c>disable_mentions</c> 避免误触发通知）。</summary>
    /// <param name="channelId">频道 id。</param>
    /// <param name="message">正文。</param>
    /// <param name="rootId">thread 根帖 id；null = 新帖。</param>
    /// <param name="fileIds">已上传的文件 id（挂在第一帖上）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>新帖 id。</returns>
    public async Task<string> CreatePostAsync(
        string channelId,
        string message,
        string? rootId,
        IReadOnlyList<string>? fileIds = null,
        CancellationToken cancellationToken = default)
    {
        var payload = new Dictionary<string, object?>
        {
            ["channel_id"] = channelId,
            ["message"] = message,
            ["props"] = new Dictionary<string, object> { ["disable_mentions"] = true },
        };
        if (!string.IsNullOrEmpty(rootId))
        {
            payload["root_id"] = rootId;
        }

        if (fileIds is { Count: > 0 })
        {
            payload["file_ids"] = fileIds;
        }

        using var response = await _http.PostAsJsonAsync("api/v4/posts", payload, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Mattermost 发帖失败：{(int)response.StatusCode} {await DescribeFailureAsync(response, cancellationToken).ConfigureAwait(false)}");
        }

        using var doc = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Mattermost /posts 无响应");
        return doc.RootElement.TryGetProperty("id", out var id) ? id.GetString() ?? string.Empty : string.Empty;
    }

    /// <summary>取附件元数据（文件名 + MIME）。</summary>
    public async Task<(string Name, string ContentType)> GetFileInfoAsync(
        string fileId,
        CancellationToken cancellationToken = default)
    {
        using var doc = await _http.GetFromJsonAsync<JsonDocument>($"api/v4/files/{fileId}/info", cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Mattermost /files/{fileId}/info 无响应");
        var root = doc.RootElement;
        return (
            root.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty,
            root.TryGetProperty("mime_type", out var mime) ? mime.GetString() ?? string.Empty : string.Empty);
    }

    /// <summary>下载附件字节（走长超时 client）。</summary>
    public async Task<byte[]> DownloadFileAsync(string fileId, CancellationToken cancellationToken = default)
    {
        using var response = await _fileHttp.GetAsync($"api/v4/files/{fileId}", cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 上传附件；返回文件 id（发帖时填 <c>file_ids</c>）。
    ///
    /// 端点是 <c>api/v4/files</c>，**没有 <c>/upload</c> 后缀**（2026-10-23 实锤：写错会 404，
    /// 且服务端在客户端还在传 body 时就关连接，表现成 "Error while copying content to a stream"）。
    /// </summary>
    public async Task<string> UploadFileAsync(
        string channelId,
        string fileName,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default)
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent(channelId), "channel_id" },
            { new ByteArrayContent(data.ToArray()), "files", fileName },
        };
        using var response = await _fileHttp.PostAsync("api/v4/files", form, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Mattermost 文件上传失败：{(int)response.StatusCode} {await DescribeFailureAsync(response, cancellationToken).ConfigureAwait(false)}");
        }

        using var doc = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Mattermost /files 无响应");
        return doc.RootElement.TryGetProperty("file_infos", out var infos)
            && infos.ValueKind == JsonValueKind.Array
            && infos.GetArrayLength() > 0
            && infos[0].TryGetProperty("id", out var id)
                ? id.GetString() ?? string.Empty
                : string.Empty;
    }

    /// <summary>typing 信号（<c>POST /api/v4/users/{id}/typing</c>，约 5s 自动过期）。</summary>
    public async Task SendTypingAsync(string userId, string channelId, CancellationToken cancellationToken = default)
    {
        using var response = await _http.PostAsJsonAsync(
            $"api/v4/users/{userId}/typing",
            new Dictionary<string, object> { ["channel_id"] = channelId },
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>失败响应体（Mattermost 的错误 JSON 里有可读原因）；读不出来就空串，不掩盖状态码。</summary>
    private static async Task<string> DescribeFailureAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return body.Length > 400 ? body[..400] : body;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _fileHttp.Dispose();
        _http.Dispose(); // handler 由 _http 持有并释放（_fileHttp 不 disposeHandler）
    }
}
