using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shoyee.Agent.Models;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Options;
using Shoyee.Tools.Media;
using Shoyee.Voice;

namespace Shoyee.Agent.Media;

/// <summary>
/// 入站附件自动分析：图片 → 视觉端点（<see cref="ModelProfiles.Vision"/>），语音 → <see cref="ISpeechTranscriber"/>。
///
/// 用 <see cref="IChatClient"/> 而不是裸 HttpClient：视觉端点和主模型一样是 OpenAI 兼容端点，
/// <see cref="DataContent"/> 会被映射成 <c>image_url</c>；key 仍由 <see cref="ModelClientFactory"/> 从环境变量读。
/// 分析失败一律降级为「不提供描述」，不拦这一轮（附件路径仍在正文里）。
/// </summary>
public sealed class AttachmentAnalyzer : IAttachmentAnalyzer
{
    private const long MaxImageBytes = 10 * 1024 * 1024;
    private const int MaxTextChars = 4_000;

    private const string DefaultImagePrompt =
        "描述这张图片的内容：主体、场景、文字信息、值得注意的细节。用中文，简洁但具体，控制在 300 字以内。";

    private volatile AttachmentOptions _options;
    private readonly ISpeechTranscriber? _transcriber;
    private readonly ILogger<AttachmentAnalyzer> _logger;
    private readonly Func<IChatClient?>? _visionFactory;
    private volatile Lazy<IChatClient?> _vision;

    /// <summary>创建分析器。</summary>
    /// <param name="options">附件选项（null = 默认开启）。</param>
    /// <param name="vision">视觉客户端工厂（懒执行；null = 不做图片分析）。</param>
    /// <param name="transcriber">转写实现（null = 不做语音转写）。</param>
    /// <param name="logger">日志。</param>
    public AttachmentAnalyzer(
        AttachmentOptions? options,
        Func<IChatClient?>? vision,
        ISpeechTranscriber? transcriber = null,
        ILogger<AttachmentAnalyzer>? logger = null)
    {
        _options = options ?? new AttachmentOptions();
        _transcriber = transcriber;
        _logger = logger ?? NullLogger<AttachmentAnalyzer>.Instance;
        _visionFactory = vision;
        _vision = new Lazy<IChatClient?>(vision ?? (static () => null));
    }

    /// <summary>按工作区配置创建（Daemon 组合根用）。</summary>
    /// <param name="config">工作区配置（附件选项 + 视觉档位）；null = 全部关闭。</param>
    /// <param name="models">模型工厂（懒创建视觉客户端；null = 不做图片分析）。</param>
    /// <param name="transcriber">转写实现（null = 不做语音转写）。</param>
    /// <param name="logger">日志。</param>
    public static AttachmentAnalyzer Create(
        WorkspaceConfig? config,
        ModelClientFactory? models,
        ISpeechTranscriber? transcriber = null,
        ILogger<AttachmentAnalyzer>? logger = null)
        => new(
            config?.Attachments,
            () => CreateVision(models, logger),
            transcriber,
            logger);

    /// <summary>热更新附件选项与视觉客户端（<c>config.reload</c> 的 attachments/models 段）。</summary>
    /// <param name="options">新的附件选项（null = 默认开启）。</param>
    public void Reload(AttachmentOptions? options)
    {
        _options = options ?? new AttachmentOptions();
        _vision = new Lazy<IChatClient?>(_visionFactory ?? (static () => null));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AttachmentAnalysis>> AnalyzeAsync(
        IReadOnlyList<MessageAttachment> attachments,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attachments);
        if (!_options.AutoAnalyze || attachments.Count == 0)
        {
            return [];
        }

        var tasks = attachments.Select(attachment => AnalyzeOneAsync(attachment, cancellationToken));
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return results.OfType<AttachmentAnalysis>().ToArray();
    }

    private Task<AttachmentAnalysis?> AnalyzeOneAsync(MessageAttachment attachment, CancellationToken cancellationToken)
        => attachment.Kind switch
        {
            AttachmentKind.Image => DescribeImageAsync(attachment, cancellationToken),
            AttachmentKind.Audio => TranscribeAsync(attachment, cancellationToken),
            _ => Task.FromResult<AttachmentAnalysis?>(null),
        };

    private async Task<AttachmentAnalysis?> DescribeImageAsync(
        MessageAttachment attachment,
        CancellationToken cancellationToken)
    {
        if (_vision.Value is not { } vision)
        {
            return null;
        }

        try
        {
            var info = new FileInfo(attachment.Path);
            if (!info.Exists || info.Length == 0 || info.Length > MaxImageBytes)
            {
                return null;
            }

            var mime = string.IsNullOrWhiteSpace(attachment.Mime)
                ? AttachmentFactory.GuessMime(attachment.Path)
                : attachment.Mime;
            if (!mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var bytes = await File.ReadAllBytesAsync(attachment.Path, cancellationToken).ConfigureAwait(false);
            var message = new ChatMessage(ChatRole.User, [new TextContent(Prompt()), new DataContent(bytes, mime)]);
            var response = await RunWithTimeoutAsync(
                    token => vision.GetResponseAsync([message], cancellationToken: token),
                    cancellationToken)
                .ConfigureAwait(false);

            var text = Trim(response?.Text);
            if (text is null)
            {
                // 思考模型的思维链算在输出预算里，被截断时 content 为空；这里只降级成「无描述」，至少要留下原因
                _logger.LogDebug(
                    "视觉端点没有返回图片描述：{Path}，finish={FinishReason}",
                    attachment.Path,
                    response?.FinishReason);
                return null;
            }

            return new AttachmentAnalysis(attachment.Path, $"图片内容（{attachment.FileName}）：{text}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "图片自动分析失败，跳过：{Path}", attachment.Path);
            return null;
        }
    }

    private async Task<AttachmentAnalysis?> TranscribeAsync(
        MessageAttachment attachment,
        CancellationToken cancellationToken)
    {
        if (_transcriber is not { Configured: true } transcriber || !File.Exists(attachment.Path))
        {
            return null;
        }

        try
        {
            var result = await RunWithTimeoutAsync(
                    token => transcriber.TranscribeAsync(attachment.Path, token),
                    cancellationToken)
                .ConfigureAwait(false);

            var text = Trim(result?.Text);
            return text is null
                ? null
                : new AttachmentAnalysis(attachment.Path, $"语音转写（{attachment.FileName}）：{text}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 转写接口约定「识别不出/不可用返回 null」，抛出来的多半是文件格式问题
            _logger.LogDebug(ex, "语音自动转写失败，跳过：{Path}", attachment.Path);
            return null;
        }
    }

    /// <summary>给单次分析加超时：超时按失败降级，不拖住整轮对话。</summary>
    private async Task<T> RunWithTimeoutAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        var seconds = _options.TimeoutSeconds <= 0 ? 60 : _options.TimeoutSeconds;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(seconds));
        try
        {
            return await action(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("附件分析超时（{Seconds}s），按失败处理", seconds);
            return default!;
        }
    }

    private string Prompt()
        => string.IsNullOrWhiteSpace(_options.ImagePrompt) ? DefaultImagePrompt : _options.ImagePrompt;

    private static string? Trim(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim();
        return trimmed.Length <= MaxTextChars ? trimmed : trimmed[..MaxTextChars] + "…";
    }

    private static IChatClient? CreateVision(ModelClientFactory? models, ILogger<AttachmentAnalyzer>? logger)
    {
        if (models is null)
        {
            return null;
        }

        try
        {
            return models.CreateVision();
        }
        catch (Exception ex)
        {
            (logger ?? NullLogger<AttachmentAnalyzer>.Instance)
                .LogWarning(ex, "视觉端点不可用，入站图片不做自动分析");
            return null;
        }
    }
}
