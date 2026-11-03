namespace Shoyee.Voice.Apple;

/// <summary>Apple 本地转写配置（<c>voice.providers.apple</c> 节）。</summary>
/// <param name="HelperPath">
/// <c>speech-stt</c> 可执行文件路径；留空 = 程序目录下的 <c>speech-stt</c>。
/// </param>
/// <param name="Language">识别语言（<c>zh-CN</c> / <c>en-US</c> 等）。</param>
/// <param name="TimeoutSeconds">单次识别超时（秒）。</param>
public sealed record AppleVoiceOptions(
    string HelperPath = "",
    string Language = "zh-CN",
    int TimeoutSeconds = 120);
