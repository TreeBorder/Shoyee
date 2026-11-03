using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Shoyee.Voice;

namespace Shoyee.Voice.Mimo;

/// <summary>
/// MiMo 语音客户端：合成（<c>mimo-v2.5-tts</c> 系列）与转写（<c>mimo-v2.5-asr</c>），
/// 两个方向都是 OpenAI 兼容的 <c>POST {baseUrl}/chat/completions</c>。
///
/// 实测要点（2026-10-28）：
/// <list type="bullet">
/// <item>合成结果在 <c>choices[0].message.audio.data</c>（base64），同时回 <c>transcript</c>；</item>
/// <item>转写结果在 <c>choices[0].message.content</c>；</item>
/// <item><c>asr_options</c> 是**请求体顶层字段**（Python SDK 的 <c>extra_body</c> 就是顶层）；</item>
/// <item>合成 24kHz 单声道 16-bit；转写只收 wav/mp3，base64 后 ≤ 10 MB。</item>
/// </list>
///
/// 只对 408/429/5xx 重试（与 Web 一致）；超时按次控制，不让 HttpClient 全局超时。
/// </summary>
public sealed class MimoSpeechProvider : ISpeechSynthesizer, ISpeechTranscriber, IDisposable
{
    /// <summary>base64 后 ≤ 10 MB ⇒ 原始字节留一点余量。</summary>
    private const int MaxUploadBytes = 7_000_000;

    private const int MaxAttempts = 3;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly MimoVoiceOptions _options;
    private readonly ILogger<MimoSpeechProvider>? _logger;
    private readonly string _baseUrl;
    private readonly TimeSpan _timeout;
    private readonly string _format;

    /// <summary>创建客户端。</summary>
    /// <param name="options">MiMo 自己的配置节。</param>
    /// <param name="http">测试可注入的 HttpClient。</param>
    /// <param name="logger">日志（可空）。</param>
    public MimoSpeechProvider(
        MimoVoiceOptions options,
        HttpClient? http = null,
        ILogger<MimoSpeechProvider>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _logger = logger;
        _http = http ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        _ownsHttp = http is null;
        _baseUrl = options.BaseUrl.TrimEnd('/');
        _timeout = TimeSpan.FromSeconds(Math.Max(5, options.TimeoutSeconds));

        _format = (options.Format ?? "wav").Trim().ToLowerInvariant();
        if (_format is not ("wav" or "mp3"))
        {
            throw new InvalidOperationException(
                $"MiMo 合成格式只支持 wav / mp3（当前配置：{options.Format}）。");
        }
    }

    /// <inheritdoc />
    public string Name => "mimo";

    /// <inheritdoc />
    public bool Configured => !string.IsNullOrWhiteSpace(_baseUrl);

    /// <inheritdoc />
    public string VoiceGuide => MimoVoiceGuide.Text;

    /// <inheritdoc />
    public async Task<SynthesizedAudio> SynthesizeAsync(
        SynthesisRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            throw new ArgumentException("要合成的文本为空。", nameof(request));
        }

        // 配了音色设计提示词就走设计模型：user 消息是音色描述，assistant 是要念的正文。
        var design = !string.IsNullOrWhiteSpace(_options.DesignPrompt);
        var messages = new List<MimoMessage>();
        if (design)
        {
            messages.Add(new MimoMessage("user", _options.DesignPrompt!));
        }
        else if (!string.IsNullOrWhiteSpace(request.Style))
        {
            messages.Add(new MimoMessage("user", request.Style!));
        }

        messages.Add(new MimoMessage("assistant", request.Text));

        var voice = design
            ? null
            : string.IsNullOrWhiteSpace(request.Voice)
                ? (_options.Voice is { Length: > 0 } configured ? configured : "mimo_default")
                : request.Voice;

        var payload = new MimoChatRequest
        {
            Model = design ? _options.DesignModel : _options.TtsModel,
            Messages = messages,
            Audio = new MimoAudioOptions(_format, voice),
        };

        var body = await SendAsync(payload, cancellationToken).ConfigureAwait(false);
        var audio = Deserialize(body).Choices?.FirstOrDefault()?.Message?.Audio;
        if (string.IsNullOrWhiteSpace(audio?.Data))
        {
            throw new InvalidOperationException(
                $"MiMo 没有返回音频（模型 {payload.Model}，音色 {voice ?? "(设计模式)"}）：检查模型 id 与音色是否有效。");
        }

        byte[] data;
        try
        {
            data = Convert.FromBase64String(audio.Data);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("MiMo 返回的音频不是合法 base64。", ex);
        }

        if (data.Length == 0)
        {
            throw new InvalidOperationException("MiMo 返回了空音频。");
        }

        _logger?.LogInformation("MiMo 语音合成完成：{Bytes} 字节（{Model}）", data.Length, payload.Model);
        return new SynthesizedAudio(
            data,
            _format == "mp3" ? "audio/mpeg" : "audio/wav",
            "." + _format,
            audio.Transcript);
    }

    /// <inheritdoc />
    public async Task<TranscriptionResult?> TranscribeAsync(
        string audioPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(audioPath);
        var extension = Path.GetExtension(audioPath).ToLowerInvariant();
        if (extension is not (".wav" or ".mp3"))
        {
            throw new NotSupportedException(
                $"MiMo 转写只收 wav / mp3（当前：{extension}）；先转码再试。");
        }

        var file = new FileInfo(audioPath);
        if (!file.Exists)
        {
            throw new FileNotFoundException("音频文件不存在。", audioPath);
        }

        if (file.Length > MaxUploadBytes)
        {
            throw new InvalidOperationException(
                $"音频 {file.Length / 1024.0 / 1024.0:F1} MB 超过 MiMo 上限（base64 后 ≤ 10 MB，原始约 7 MB）。");
        }

        var base64 = Convert.ToBase64String(await File.ReadAllBytesAsync(audioPath, cancellationToken).ConfigureAwait(false));
        var mime = extension == ".mp3" ? "audio/mpeg" : "audio/wav";
        var payload = new MimoChatRequest
        {
            Model = _options.AsrModel,
            Messages =
            [
                new MimoMessage("user", new object[]
                {
                    new MimoContentPart("input_audio", new MimoAudioInput($"data:{mime};base64,{base64}")),
                }),
            ],
            AsrOptions = new MimoAsrOptions(string.IsNullOrWhiteSpace(_options.Language) ? "auto" : _options.Language),
        };

        var body = await SendAsync(payload, cancellationToken).ConfigureAwait(false);
        var text = Deserialize(body).Choices?.FirstOrDefault()?.Message?.Content;
        if (string.IsNullOrWhiteSpace(text))
        {
            _logger?.LogWarning("MiMo 转写返回空文本：{File}", Path.GetFileName(audioPath));
            return null;
        }

        _logger?.LogInformation("MiMo 转写完成：{File} → {Chars} 字", Path.GetFileName(audioPath), text.Length);
        return new TranscriptionResult(text.Trim());
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsHttp)
        {
            _http.Dispose();
        }
    }

    private async Task<string> SendAsync(MimoChatRequest payload, CancellationToken cancellationToken)
    {
        var apiKey = ResolveApiKey();
        var json = JsonSerializer.Serialize(payload, JsonOptions);

        for (var attempt = 1; ; attempt++)
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(_timeout);

            using var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/chat/completions")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, timeoutSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"语音请求超时（{_timeout.TotalSeconds:F0}s）。");
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
                    $"语音接口 HTTP {(int)response.StatusCode}：{Cut(body, 300)}",
                    null,
                    response.StatusCode);
            }
        }
    }

    private string ResolveApiKey()
    {
        var envName = string.IsNullOrWhiteSpace(_options.ApiKeyEnv) ? "XIAOMI_API_KEY" : _options.ApiKeyEnv;
        if (Environment.GetEnvironmentVariable(envName) is { Length: > 0 } key)
        {
            return key;
        }

        throw new InvalidOperationException(
            $"语音工具缺少 API key：设置环境变量 {envName}（对应 voice.providers.mimo.apiKeyEnv）。");
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

    private static MimoChatResponse Deserialize(string body)
    {
        try
        {
            return JsonSerializer.Deserialize<MimoChatResponse>(body, JsonOptions)
                   ?? throw new JsonException("响应是空的。");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("语音接口返回了无法解析的 JSON：" + ex.Message, ex);
        }
    }

    private static string Cut(string text, int max)
        => text.Length <= max ? text : text[..max] + "\n…（已截断）";
}
