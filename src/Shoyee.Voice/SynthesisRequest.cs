namespace Shoyee.Voice;

/// <summary>一次合成请求。</summary>
/// <param name="Text">要合成的文本（必填）。可含供应商支持的风格/音频标签。</param>
/// <param name="Style">
/// 自然语言风格指令（可空），如「温柔一点，语速稍慢」。各家的落地方式不同：
/// Mimo 预置音色模型把它放进 <c>user</c> 消息。
/// </param>
/// <param name="Voice">音色 id（可空 = 用配置里的默认音色）。</param>
public sealed record SynthesisRequest(
    string Text,
    string? Style = null,
    string? Voice = null);
