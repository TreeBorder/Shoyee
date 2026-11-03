namespace Shoyee.Voice.Mimo;

/// <summary>MiMo 语音配置（<c>voice.providers.mimo</c> 节）。</summary>
/// <param name="BaseUrl">OpenAI 兼容根地址（不含 <c>/chat/completions</c>）。</param>
/// <param name="ApiKeyEnv">API key 的环境变量名（MiMo 的 key 与模型端点分开，默认 <c>XIAOMI_API_KEY</c>）。</param>
/// <param name="TtsModel">预置音色合成模型。</param>
/// <param name="Voice">默认音色 id；留空用 <c>mimo_default</c>。</param>
/// <param name="DesignPrompt">
/// 音色设计提示词（可空）。**配了就用设计模型**（<paramref name="DesignModel"/>），忽略 <paramref name="Voice"/>；
/// 不配则走预置音色。筱义是固定人设，通常配一个设计提示词或一个预置音色二选一。
/// </param>
/// <param name="DesignModel">音色设计模型。</param>
/// <param name="Format">输出格式：<c>wav</c>（默认）或 <c>mp3</c>。</param>
/// <param name="AsrModel">转写模型。</param>
/// <param name="Language">转写语种：<c>auto</c> / <c>zh</c> / <c>en</c>。</param>
/// <param name="TimeoutSeconds">单次请求超时（秒；合成长文会慢，别调太小）。</param>
public sealed record MimoVoiceOptions(
    string BaseUrl = "https://api.xiaomimimo.com/v1",
    string ApiKeyEnv = "XIAOMI_API_KEY",
    string TtsModel = "mimo-v2.5-tts",
    string? Voice = "mimo_default",
    string? DesignPrompt = null,
    string DesignModel = "mimo-v2.5-tts-voicedesign",
    string Format = "wav",
    string AsrModel = "mimo-v2.5-asr",
    string Language = "zh",
    int TimeoutSeconds = 120);
