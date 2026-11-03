namespace Shoyee.Voice;

/// <summary>
/// 语音合成（TTS）能力。一个实现 = 一家供应商；<see cref="Shoyee.Voice"/> 只放契约，
/// 具体实现在 <c>Shoyee.Voice.Mimo</c> 等项目里。
///
/// 约定：实现**不碰文件系统**——只返回音频字节，落盘由工具层决定（当前是
/// <c>{workspace}/assets/audio/YYYY/MM/</c>），这样 provider 不用知道工作区在哪。
/// </summary>
public interface ISpeechSynthesizer
{
    /// <summary>provider key（与 <c>voice.providers.{key}</c> 一致，诊断与多供应商并存时区分）。</summary>
    string Name { get; }

    /// <summary>是否配置好了可用的合成服务（未配置时语音合成工具不挂载）。</summary>
    bool Configured { get; }

    /// <summary>
    /// 给模型看的音色/风格手册（音色 id、设计提示词写法、标签语法等）。
    /// 由实现方提供：标签体系是各家的方言，写死在工具里就会跟着供应商一起错。
    /// </summary>
    string VoiceGuide { get; }

    /// <summary>合成一段语音；失败抛异常（工具层把消息转成给模型看的可操作提示）。</summary>
    /// <param name="request">要合成的文本与风格控制。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<SynthesizedAudio> SynthesizeAsync(
        SynthesisRequest request,
        CancellationToken cancellationToken = default);
}
