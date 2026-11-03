namespace Shoyee.Agent.Media;

/// <summary>一条附件的自动分析结果（给模型看的一句话说明）。</summary>
/// <param name="Path">附件本地路径（与 <c>MessageAttachment.Path</c> 对应）。</param>
/// <param name="Text">分析文本（带「图片内容 / 语音转写」前缀）。</param>
public sealed record AttachmentAnalysis(string Path, string Text);

/// <summary>
/// 入站附件进模型上下文前的自动分析：图片走视觉端点，语音走转写。
///
/// 只在「能分析且分析成功」时返回结果；没配对应能力、文件读不了、超时都直接跳过，
/// 模型仍能从正文拿到本地路径并自行调用 <c>analyze_image</c> / <c>voice_transcribe</c>。
/// </summary>
public interface IAttachmentAnalyzer
{
    /// <summary>分析一组附件；失败项不出现在结果里，取消会抛 <see cref="OperationCanceledException"/>。</summary>
    /// <param name="attachments">本轮附件。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<IReadOnlyList<AttachmentAnalysis>> AnalyzeAsync(
        IReadOnlyList<Contracts.Dtos.MessageAttachment> attachments,
        CancellationToken cancellationToken = default);
}
