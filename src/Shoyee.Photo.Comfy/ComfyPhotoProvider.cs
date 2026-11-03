using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Shoyee.Photo.Comfy;

/// <summary>
/// ComfyUI 供应商（照 Harem <c>ComfyUiClient</c> + <c>WorkflowBuilderService</c>）：
/// 模板变量替换 → <c>POST /prompt</c> → 轮询 <c>GET /history/{id}</c> → <c>GET /view</c> 取回字节。
///
/// 模板占位符：<c>{{PROMPT}}</c>、<c>{{NEGATIVE_PROMPT}}</c>、<c>{{SEED}}</c>、<c>{{WIDTH}}</c>、
/// <c>{{HEIGHT}}</c>、<c>{{STEPS}}</c>、<c>{{CFG}}</c>、<c>{{ROLE_NAME}}</c>、<c>{{FILENAME_PREFIX}}</c>。
/// 负向词含 <c>face visible</c> 时换用无脸工作流——这是模板约定的能力探测，不是硬编码某个人设。
/// </summary>
public sealed class ComfyPhotoProvider : IPhotoProvider
{
    /// <summary>DI 里的 HttpClient 名称。</summary>
    public const string HttpClientName = "ShoyeePhotoComfy";

    private readonly ComfyPhotoOptions _options;
    private readonly string _workspaceRoot;
    private readonly HttpClient _http;
    private readonly ILogger<ComfyPhotoProvider> _logger;

    /// <summary>创建供应商。</summary>
    /// <param name="options">配置。</param>
    /// <param name="workspaceRoot">工作区根（模板路径相对它）。</param>
    /// <param name="http">HTTP 客户端。</param>
    /// <param name="logger">日志。</param>
    public ComfyPhotoProvider(
        ComfyPhotoOptions options,
        string workspaceRoot,
        HttpClient http,
        ILogger<ComfyPhotoProvider>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentNullException.ThrowIfNull(http);
        _options = options;
        _workspaceRoot = workspaceRoot;
        _http = http;
        _logger = logger ?? NullLogger<ComfyPhotoProvider>.Instance;
    }

    /// <inheritdoc />
    public string Name => "comfy";

    /// <inheritdoc />
    public bool Configured => !string.IsNullOrWhiteSpace(_options.Endpoint);

    private string BaseUrl => _options.Endpoint.TrimEnd('/');

    /// <inheritdoc />
    public async Task<PhotoResult> GenerateAsync(PhotoRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var workflow = await BuildWorkflowAsync(request, cancellationToken).ConfigureAwait(false);
        var promptId = await QueueAsync(workflow, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("ComfyUI 已入队：{PromptId}（{Width}x{Height}，seed {Seed}）",
            promptId, request.Width, request.Height, request.Seed);

        var images = await WaitForImagesAsync(promptId, cancellationToken).ConfigureAwait(false);
        if (images.Count == 0)
        {
            throw new InvalidOperationException($"ComfyUI 任务 {promptId} 完成但没有输出图片。");
        }

        var results = new List<PhotoImage>(images.Count);
        foreach (var image in images)
        {
            results.Add(await DownloadAsync(image, cancellationToken).ConfigureAwait(false));
        }

        return new PhotoResult(results);
    }

    private async Task<string> BuildWorkflowAsync(PhotoRequest request, CancellationToken cancellationToken)
    {
        var fileName = request.Negative.Contains("face visible", StringComparison.OrdinalIgnoreCase)
            ? _options.NoFaceWorkflow
            : _options.Workflow;
        var path = Path.Combine(_workspaceRoot, _options.WorkflowsDir, fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"工作流模板不存在：{path}", path);
        }

        var template = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        return template
            .Replace("{{PROMPT}}", EscapeJson(request.Positive), StringComparison.Ordinal)
            .Replace("{{NEGATIVE_PROMPT}}", EscapeJson(request.Negative), StringComparison.Ordinal)
            .Replace("{{SEED}}", request.Seed.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{{WIDTH}}", request.Width.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{{HEIGHT}}", request.Height.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{{STEPS}}", _options.Steps.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{{CFG}}", _options.Cfg.ToString("0.0", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{{ROLE_NAME}}", request.FileNamePrefix ?? "image", StringComparison.Ordinal)
            .Replace("{{FILENAME_PREFIX}}", request.FileNamePrefix ?? "image", StringComparison.Ordinal);
    }

    private async Task<string> QueueAsync(string workflow, CancellationToken cancellationToken)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(workflow);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("工作流模板不是合法 JSON：" + ex.Message, ex);
        }

        if (node is null)
        {
            throw new InvalidOperationException("工作流模板是空的。");
        }

        var body = new JsonObject { ["prompt"] = node };
        using var response = await _http
            .PostAsync(
                $"{BaseUrl}/prompt",
                new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
                cancellationToken)
            .ConfigureAwait(false);

        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"ComfyUI 拒绝任务（HTTP {(int)response.StatusCode}）：{Truncate(text)}");
        }

        using var document = JsonDocument.Parse(text);
        if (!document.RootElement.TryGetProperty("prompt_id", out var id)
            || id.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException("ComfyUI 没有返回 prompt_id：" + Truncate(text));
        }

        return id.GetString()!;
    }

    private async Task<List<ComfyImageRef>> WaitForImagesAsync(string promptId, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(Math.Max(1, _options.TimeoutSeconds));
        var interval = TimeSpan.FromSeconds(Math.Max(1, _options.PollIntervalSeconds));

        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(interval, cancellationToken).ConfigureAwait(false);

            using var response = await _http
                .GetAsync($"{BaseUrl}/history/{Uri.EscapeDataString(promptId)}", cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                continue;
            }

            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var images = TryExtractImages(text, promptId);
            if (images is { Count: > 0 })
            {
                return images;
            }
        }

        throw new TimeoutException(
            $"ComfyUI 出图超时（{_options.TimeoutSeconds}s，prompt_id={promptId}）。");
    }

    private static List<ComfyImageRef>? TryExtractImages(string historyJson, string promptId)
    {
        if (string.IsNullOrWhiteSpace(historyJson))
        {
            return null;
        }

        using var document = JsonDocument.Parse(historyJson);
        if (!document.RootElement.TryGetProperty(promptId, out var entry))
        {
            return null;
        }

        if (!entry.TryGetProperty("outputs", out var outputs) || outputs.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var images = new List<ComfyImageRef>();
        foreach (var node in outputs.EnumerateObject())
        {
            if (!node.Value.TryGetProperty("images", out var array) || array.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var image in array.EnumerateArray())
            {
                var filename = image.TryGetProperty("filename", out var f) ? f.GetString() : null;
                if (string.IsNullOrWhiteSpace(filename))
                {
                    continue;
                }

                var subfolder = image.TryGetProperty("subfolder", out var s) ? s.GetString() : null;
                var type = image.TryGetProperty("type", out var t) ? t.GetString() : "output";
                images.Add(new ComfyImageRef(filename, subfolder, type ?? "output"));
            }
        }

        return images;
    }

    private async Task<PhotoImage> DownloadAsync(ComfyImageRef image, CancellationToken cancellationToken)
    {
        var url = $"{BaseUrl}/view?filename={Uri.EscapeDataString(image.FileName)}";
        if (!string.IsNullOrEmpty(image.Subfolder))
        {
            url += $"&subfolder={Uri.EscapeDataString(image.Subfolder)}";
        }

        url += $"&type={Uri.EscapeDataString(image.Type)}";

        using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        var mime = response.Content.Headers.ContentType?.MediaType ?? "image/png";
        var extension = Path.GetExtension(image.FileName);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = mime switch
            {
                "image/jpeg" => ".jpg",
                "image/webp" => ".webp",
                _ => ".png",
            };
        }

        return new PhotoImage(mime, bytes, extension);
    }

    private static string EscapeJson(string value) => JsonSerializer.Serialize(value).Trim('"');

    private static string Truncate(string value)
        => value.Length <= 500 ? value : value[..500] + "…";

    private sealed record ComfyImageRef(string FileName, string? Subfolder, string Type);
}
