namespace Shoyee.Voice;

/// <summary>
/// 语音转写（STT）能力。不是所有供应商都有（Apple 只做转写，Mimo 两向都有），
/// 所以和 <see cref="ISpeechSynthesizer"/> 分开成两个接口，用 <c>is</c> / DI 探测。
///
/// 约定：识别不出内容、引擎不可用、超时一律返回 <c>null</c>，**不抛**——转写只是让消息更可读，
/// 不能因为它挡住宿主流程（Secretary 的踩坑结论）。
/// 但**输入不合格**（文件不存在、格式不在支持列表）属于可操作的配置/输入错误，直接抛异常，
/// 让工具层把原因告诉模型，而不是默默返回 null。
/// </summary>
public interface ISpeechTranscriber
{
    /// <summary>provider key（与 <c>voice.providers.{key}</c> 一致）。</summary>
    string Name { get; }

    /// <summary>是否配置好了可用的转写服务（未配置时转写工具不挂载）。</summary>
    bool Configured { get; }

    /// <summary>转写一个本地音频文件；不是可转写音频、失败或超时返回 <c>null</c>。</summary>
    /// <param name="audioPath">音频文件绝对路径。</param>
    /// <param name="cancellationToken">取消令牌（取消会抛 <see cref="OperationCanceledException"/>）。</param>
    Task<TranscriptionResult?> TranscribeAsync(
        string audioPath,
        CancellationToken cancellationToken = default);
}
