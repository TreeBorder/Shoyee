using System.ComponentModel;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Shoyee.Contracts.Options;
using Shoyee.Tools.Composition;
using Shoyee.Tools.Files;

namespace Shoyee.Tools.Media;

/// <summary>
/// 图片分析工具 <c>analyze_image</c>：OpenAI 兼容视觉端点的 <c>image_url</c> content block，
/// 本地图片 → base64 data URL → <c>POST {baseUrl}/chat/completions</c>。
/// 端点与主模型解耦（<see cref="ModelProfiles.Vision"/>）；不引 OpenAI SDK，裸 HttpClient 调一次。
///
/// 思考模型（如 deepseek-flash）默认开着思考、effort 默认 high，思维链算在 <c>max_tokens</c> 里：
/// 图复杂时思考吃光预算，<c>finish_reason=length</c> 且 <c>content</c> 为空，表现为「没有返回内容」。
/// 所以这里默认带 <c>{"thinking":{"type":"disabled"}}</c>（DeepSeek 官方开关），被端点以 400 拒掉时
/// 去掉该字段重试一次；输出仍被截断则加大预算再试一次，空结果按 <c>finish_reason</c> 报清原因。
/// </summary>
public static class ImageAnalysisTool
{
    /// <summary>工具名（MCP 防冒名保留名）。</summary>
    public const string ToolName = "analyze_image";

    /// <summary>单次分析超时。</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(120);

    /// <summary>图片大小上限（base64 膨胀后进请求体）。</summary>
    private const long MaxImageBytes = 10 * 1024 * 1024;

    /// <summary>首次请求的输出预算。关掉思考后 2000 常常不够（一屏截图能写上千字）。</summary>
    private const int InitialMaxTokens = 4096;

    /// <summary>被 <c>finish_reason=length</c> 截断时的重试预算。</summary>
    private const int RetryMaxTokens = 8192;

    /// <summary>关闭思考模式的请求字段（DeepSeek 官方 OpenAI 兼容参数）。</summary>
    private const string ThinkingDisabledField = "thinking";

    /// <summary>构建工具；未配置视觉端点时返回 null（不挂载）。</summary>
    /// <param name="config">工作区配置（路径黑名单用）。</param>
    /// <param name="vision">视觉端点（null = 未配置）。</param>
    /// <param name="client">测试可注入的 HttpClient。</param>
    /// <param name="pathPolicy">共享路径策略（null = 按配置新建）。</param>
    public static ToolDescriptor? Create(
        WorkspaceConfig config,
        ModelEndpoint? vision,
        HttpClient? client = null,
        ToolPathPolicy? pathPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (vision is null || string.IsNullOrWhiteSpace(vision.BaseUrl) || string.IsNullOrWhiteSpace(vision.ModelId))
        {
            return null;
        }

        pathPolicy ??= ToolPathPolicy.FromConfig(config);
        var http = client ?? new HttpClient { Timeout = Timeout };
        var endpoint = new Uri(new Uri(vision.BaseUrl.TrimEnd('/') + "/"), "chat/completions");
        var tool = AIFunctionFactory.Create(
            async (
                [Description("一个或多个图片的本地绝对路径。")] string[] imagePaths,
                [Description("想问什么（可空：空 = 让模型自行描述图片内容）。多图时问题对整组图片提问。")] string? question = null,
                CancellationToken cancellationToken = default) =>
            {
                if (imagePaths is not { Length: > 0 })
                {
                    return "🖼 没给图片路径：传一个或多个图片的绝对路径。";
                }

                var dataUrls = new List<string>(imagePaths.Length);
                foreach (var imagePath in imagePaths)
                {
                    if (!string.IsNullOrWhiteSpace(imagePath)
                        && pathPolicy.Check(Path.GetFullPath(imagePath)) is { } denied)
                    {
                        return "🖼 " + denied;
                    }

                    var error = Validate(imagePath);
                    if (error is not null)
                    {
                        return error;
                    }

                    var bytes = await File.ReadAllBytesAsync(imagePath, cancellationToken).ConfigureAwait(false);
                    dataUrls.Add($"data:{MimeType(imagePath)};base64,{Convert.ToBase64String(bytes)}");
                }

                var apiKey = Environment.GetEnvironmentVariable(vision.ApiKeyEnv ?? string.Empty);

                async Task<Attempt> SendAsync(int maxTokens, bool thinkingSwitch)
                    => await PostAsync(
                            http,
                            endpoint,
                            apiKey,
                            BuildPayload(vision.ModelId, dataUrls, question, maxTokens, thinkingSwitch),
                            cancellationToken)
                        .ConfigureAwait(false);

                try
                {
                    var thinkingSwitch = true;
                    var attempt = await SendAsync(InitialMaxTokens, thinkingSwitch).ConfigureAwait(false);

                    // 非 DeepSeek 端点可能拒收 thinking 字段：去掉它重发一次，别把工具能力绑死在一家
                    if (attempt.Status == HttpStatusCode.BadRequest && MentionsThinking(attempt.Body))
                    {
                        thinkingSwitch = false;
                        attempt = await SendAsync(InitialMaxTokens, thinkingSwitch).ConfigureAwait(false);
                    }

                    if (!attempt.IsSuccess)
                    {
                        return Failure(attempt);
                    }

                    var completion = Parse(attempt.Body);
                    if (completion.Text is null && completion.FinishReason == "length")
                    {
                        attempt = await SendAsync(RetryMaxTokens, thinkingSwitch).ConfigureAwait(false);
                        if (!attempt.IsSuccess)
                        {
                            return Failure(attempt);
                        }

                        completion = Parse(attempt.Body);
                    }

                    return completion.Text ?? Diagnose(completion);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    return "🖼 图片分析失败：" + ex.Message;
                }
            },
            new AIFunctionFactoryOptions
            {
                Name = ToolName,
                Description = "看图：传入一个或多个本地图片绝对路径，可选提问。适合渠道收件箱里的附件；未配置视觉端点时不挂载。",
            });

        return ToolDescriptor.Create(tool, ToolScope.All, writes: false, requiresApproval: false);
    }

    private static string Failure(Attempt attempt)
        => $"🖼 图片分析失败：HTTP {(int)attempt.Status} {Truncate(attempt.Body, 300)}";

    private static string? Validate(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "🖼 图片路径是空的。";
        }

        var info = new FileInfo(path);
        if (!info.Exists)
        {
            return $"🖼 图片不存在：{path}";
        }

        if (info.Length > MaxImageBytes)
        {
            return $"🖼 图片太大（{info.Length / 1024 / 1024} MB，上限 {MaxImageBytes / 1024 / 1024} MB）：{path}";
        }

        return MimeType(path) is null ? $"🖼 不支持的图片类型：{path}（用 png/jpg/jpeg/webp/gif）" : null;
    }

    private static string? MimeType(string path)
        => Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => null,
        };

    private static string BuildPayload(
        string model,
        IReadOnlyList<string> dataUrls,
        string? question,
        int maxTokens,
        bool thinkingSwitch)
    {
        var content = new List<object>
        {
            new { type = "text", text = string.IsNullOrWhiteSpace(question) ? "描述这张/这些图片的内容。" : question },
        };
        content.AddRange(dataUrls.Select(static url => (object)new { type = "image_url", image_url = new { url } }));

        var payload = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["messages"] = new[] { new { role = "user", content } },
            ["max_tokens"] = maxTokens,
        };
        if (thinkingSwitch)
        {
            payload[ThinkingDisabledField] = new { type = "disabled" };
        }

        return JsonSerializer.Serialize(payload);
    }

    private static async Task<Attempt> PostAsync(
        HttpClient http,
        Uri endpoint,
        string? apiKey,
        string payload,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        if (!string.IsNullOrEmpty(apiKey))
        {
            request.Headers.Authorization = new("Bearer", apiKey);
        }

        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return new Attempt(response.StatusCode, body);
    }

    /// <summary>400 且回包提到 thinking，说明端点不认这个字段（而不是图片有问题）。</summary>
    private static bool MentionsThinking(string body)
        => body.Contains(ThinkingDisabledField, StringComparison.OrdinalIgnoreCase);

    private static Completion Parse(string body)
    {
        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.ValueKind != JsonValueKind.Object
            || !doc.RootElement.TryGetProperty("choices", out var choices)
            || choices.ValueKind != JsonValueKind.Array
            || choices.GetArrayLength() == 0
            || choices[0].ValueKind != JsonValueKind.Object)
        {
            return Completion.Empty;
        }

        var choice = choices[0];
        var finishReason = choice.TryGetProperty("finish_reason", out var finish) && finish.ValueKind == JsonValueKind.String
            ? finish.GetString()
            : null;
        int? completionTokens = doc.RootElement.TryGetProperty("usage", out var usage)
            && usage.ValueKind == JsonValueKind.Object
            && usage.TryGetProperty("completion_tokens", out var tokens)
            && tokens.TryGetInt32(out var tokenCount)
                ? tokenCount
                : null;

        if (!choice.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object)
        {
            return new Completion(null, null, null, finishReason, completionTokens);
        }

        return new Completion(
            ReadContent(message),
            ReadString(message, "reasoning_content"),
            ReadString(message, "refusal"),
            finishReason,
            completionTokens);
    }

    /// <summary>content 可能是字符串、null，或（部分网关）分块数组。</summary>
    private static string? ReadContent(JsonElement message)
    {
        if (!message.TryGetProperty("content", out var content))
        {
            return null;
        }

        if (content.ValueKind == JsonValueKind.String)
        {
            var text = content.GetString();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }

        if (content.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var parts = content.EnumerateArray()
            .Where(static part => part.ValueKind == JsonValueKind.Object)
            .Select(static part => ReadString(part, "text"))
            .Where(static text => !string.IsNullOrWhiteSpace(text));
        var joined = string.Concat(parts);
        return string.IsNullOrWhiteSpace(joined) ? null : joined;
    }

    private static string? ReadString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string Diagnose(Completion completion)
    {
        if (!string.IsNullOrWhiteSpace(completion.Refusal))
        {
            return $"🖼 视觉端点拒绝了这次请求：{Truncate(completion.Refusal, 200)}";
        }

        if (completion.FinishReason == "length")
        {
            var used = completion.CompletionTokens is { } tokens ? $"输出 {tokens} tokens" : "输出已达上限";
            return $"🖼 视觉端点输出被截断（finish_reason=length，{used}）：只拿到思考内容、没拿到答案。可缩小图片或减少提问后重试。";
        }

        if (!string.IsNullOrWhiteSpace(completion.Reasoning))
        {
            return "🖼 视觉端点只返回了思考内容、没有最终答案（思考模式没关掉或输出上限太小）。";
        }

        return $"🖼 视觉端点没有返回内容（finish_reason={completion.FinishReason ?? "无"}）。";
    }

    private static string Truncate(string text, int max)
        => text.Length <= max ? text : text[..max] + "…";

    private sealed record Attempt(HttpStatusCode Status, string Body)
    {
        public bool IsSuccess => (int)Status is >= 200 and < 300;
    }

    private sealed record Completion(
        string? Text,
        string? Reasoning,
        string? Refusal,
        string? FinishReason,
        int? CompletionTokens)
    {
        public static Completion Empty { get; } = new(null, null, null, null, null);
    }
}
