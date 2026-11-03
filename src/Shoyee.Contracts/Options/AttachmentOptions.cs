namespace Shoyee.Contracts.Options;

/// <summary>
/// 入站附件进上下文前的自动分析（图片 → 视觉描述，语音 → 转写）。
///
/// 默认开启：用户发来图片/语音时不必她再调一次 <c>analyze_image</c> / <c>voice_transcribe</c>。
/// 分析失败只降级（模型仍拿得到本地路径），不拦这一轮；没配视觉/转写能力时自动跳过。
/// </summary>
/// <param name="AutoAnalyze">是否在每轮开始前自动分析附件（默认开）。</param>
/// <param name="ImagePrompt">让视觉模型描述图片时用的提示词；空 = 用内置默认。</param>
/// <param name="TimeoutSeconds">单次分析（视觉/转写各一次）的超时；超时按失败降级。</param>
public sealed record AttachmentOptions(
    bool AutoAnalyze = true,
    string? ImagePrompt = null,
    int TimeoutSeconds = 60);
