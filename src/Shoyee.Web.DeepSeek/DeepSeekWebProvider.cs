using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Shoyee.Contracts.Options;
using Shoyee.Web;

namespace Shoyee.Web.DeepSeek;

/// <summary>
/// DeepSeek 网页搜索/抓取客户端（<c>POST /v1/tools/search</c>、<c>POST /v1/tools/fetch</c>）。
/// 只对 408/429/5xx 重试（与 Hound 一致）；超时按次控制，不让 HttpClient 全局超时。
/// API key 优先读 <see cref="WebOptions.ApiKeyEnv"/>，没配就回落主/小模型端点的 key 环境变量。
/// </summary>
public sealed class DeepSeekWebProvider : IWebProvider, IDisposable
{
    /// <summary>搜索结果正文单项上限（needContent 时防一次塞爆上下文）。</summary>
    private const int MaxSearchContentChars = 8_000;

    /// <summary>抓取正文上限。</summary>
    private const int MaxFetchContentChars = 200_000;

    private const int MaxAttempts = 3;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly DeepSeekWebOptions _options;
    private readonly ModelProfiles? _models;
    private readonly string _baseUrl;
    private readonly TimeSpan _timeout;

    /// <summary>创建客户端。</summary>
    /// <param name="options">DeepSeek 自己的配置节。</param>
    /// <param name="models">模型档位（key 没配在 web 节时回落用；可空）。</param>
    /// <param name="http">测试可注入的 HttpClient。</param>
    public DeepSeekWebProvider(DeepSeekWebOptions options, ModelProfiles? models = null, HttpClient? http = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _models = models;
        _http = http ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        _ownsHttp = http is null;
        _baseUrl = options.BaseUrl.TrimEnd('/');
        _timeout = TimeSpan.FromSeconds(Math.Max(1, options.TimeoutSeconds));
    }

    /// <inheritdoc />
    public async Task<WebSearchResponse> SearchAsync(
        string query,
        int count = 10,
        string? oriQuestion = null,
        string? localization = null,
        bool needContent = false,
        CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(
            new SearchRequest
            {
                Query = query,
                Count = count,
                OriQuestion = oriQuestion,
                Localization = localization,
                NeedContent = needContent,
            },
            JsonOptions);

        var body = await SendAsync("/v1/tools/search", payload, cancellationToken).ConfigureAwait(false);
        var response = Deserialize<SearchResponse>(body);
        return new WebSearchResponse(
            response.Results.Select(static result => new WebSearchHit(
                result.Title,
                result.Url,
                result.Snippet,
                result.Summary,
                Cut(result.Content ?? string.Empty, MaxSearchContentChars),
                result.SiteName,
                result.Date,
                result.RerankScore,
                result.Authority)).ToList());
    }

    /// <inheritdoc />
    public async Task<WebFetchResponse> FetchAsync(
        string url,
        bool decodeEscapes = true,
        CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(new FetchRequest { Url = url }, JsonOptions);
        var body = await SendAsync("/v1/tools/fetch", payload, cancellationToken).ConfigureAwait(false);
        var response = Deserialize<FetchResponse>(body);
        var content = Cut(response.Content ?? string.Empty, MaxFetchContentChars);
        return new WebFetchResponse(
            response.Title ?? string.Empty,
            decodeEscapes ? MarkdownEscapes.Unescape(content) : content);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsHttp)
        {
            _http.Dispose();
        }
    }

    private async Task<string> SendAsync(string path, string payload, CancellationToken cancellationToken)
    {
        var apiKey = ResolveApiKey();
        for (var attempt = 1; ; attempt++)
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(_timeout);

            using var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl + path)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, timeoutSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("请求超时。");
            }

            using (response)
            {
                var body = await response.Content.ReadAsStringAsync(timeoutSource.Token).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    return body;
                }

                if (attempt < MaxAttempts && IsRetryable(response.StatusCode))
                {
                    await Task.Delay(GetRetryDelay(response, attempt), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                throw new HttpRequestException(
                    $"HTTP {(int)response.StatusCode}：{Cut(body, 300)}",
                    null,
                    response.StatusCode);
            }
        }
    }

    private string ResolveApiKey()
    {
        var names = new[]
        {
            _options.ApiKeyEnv,
            _models?.Main.ApiKeyEnv,
            _models?.Small.ApiKeyEnv,
        };
        foreach (var name in names)
        {
            if (!string.IsNullOrWhiteSpace(name)
                && Environment.GetEnvironmentVariable(name) is { Length: > 0 } key)
            {
                return key;
            }
        }

        throw new InvalidOperationException(
            "网页工具缺少 API key：配置 web.apiKeyEnv，或设置主/小模型端点的 key 环境变量（默认 DEEPSEEK_API_KEY）。");
    }

    private static bool IsRetryable(HttpStatusCode statusCode)
        => statusCode is HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;

    private static TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta)
        {
            return delta;
        }

        if (response.Headers.RetryAfter?.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                return wait > TimeSpan.FromSeconds(10) ? TimeSpan.FromSeconds(10) : wait;
            }
        }

        return TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt - 1));
    }

    private static T Deserialize<T>(string body)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(body, JsonOptions)
                   ?? throw new JsonException("响应是空的。");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("网页接口返回了无法解析的 JSON：" + ex.Message, ex);
        }
    }

    private static string Cut(string text, int max)
        => text.Length <= max ? text : text[..max] + "\n…（已截断）";

    private sealed class SearchRequest
    {
        [JsonPropertyName("query")]
        public required string Query { get; init; }

        [JsonPropertyName("count")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? Count { get; init; }

        [JsonPropertyName("ori_question")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? OriQuestion { get; init; }

        [JsonPropertyName("localization")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Localization { get; init; }

        [JsonPropertyName("need_content")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? NeedContent { get; init; }
    }

    private sealed class SearchResponse
    {
        [JsonPropertyName("results")]
        public List<SearchResult> Results { get; init; } = [];
    }

    private sealed class SearchResult
    {
        [JsonPropertyName("title")]
        public string Title { get; init; } = string.Empty;

        [JsonPropertyName("url")]
        public string Url { get; init; } = string.Empty;

        [JsonPropertyName("snippet")]
        public string? Snippet { get; init; }

        [JsonPropertyName("summary")]
        public string? Summary { get; init; }

        [JsonPropertyName("content")]
        public string? Content { get; init; }

        [JsonPropertyName("site_name")]
        public string? SiteName { get; init; }

        [JsonPropertyName("date")]
        public string? Date { get; init; }

        [JsonPropertyName("rerank_score")]
        public double RerankScore { get; init; }

        [JsonPropertyName("authority")]
        public int Authority { get; init; }
    }

    private sealed class FetchRequest
    {
        [JsonPropertyName("url")]
        public required string Url { get; init; }
    }

    private sealed class FetchResponse
    {
        [JsonPropertyName("title")]
        public string? Title { get; init; }

        [JsonPropertyName("content")]
        public string? Content { get; init; }
    }
}
