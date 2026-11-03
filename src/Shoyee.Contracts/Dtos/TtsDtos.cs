namespace Shoyee.Contracts.Dtos;

/// <summary>
/// <c>tts.synthesize</c> 的请求：由桌面「播放」按钮触发。
///
/// <paramref name="MessageId"/> 与 <paramref name="Text"/> 二选一：给 id 时用该消息的可见正文，
/// 给 text 时直接合成传入文本。都不给或都为空 → <c>invalid_params</c>。
/// </summary>
/// <param name="MessageId">要朗读的消息 id（可见正文非空）。</param>
/// <param name="Text">直接合成的文本（不复用历史消息时用）。</param>
/// <param name="Style">自然语言风格指令（可空）。</param>
/// <param name="Voice">音色 id（可空 = 配置里的默认音色）。</param>
public sealed record TtsSynthesizeRequest(
    long? MessageId = null,
    string? Text = null,
    string? Style = null,
    string? Voice = null);

/// <summary>
/// <c>tts.synthesize</c> 的返回：已落盘的音频资产。
///
/// <paramref name="AssetId"/> 同时是 <c>asset.get</c> 的入参；
/// <paramref name="Path"/> 是绝对路径，同机客户端可直接读盘播放。
/// </summary>
/// <param name="AssetId">资产 id（见 <c>asset.get</c>）。</param>
/// <param name="Path">音频文件绝对路径。</param>
/// <param name="Mime">MIME 类型。</param>
/// <param name="SizeBytes">字节数。</param>
/// <param name="DurationMs">时长（毫秒；容器解析不出时 null）。</param>
public sealed record TtsSynthesizeResult(
    string AssetId,
    string Path,
    string Mime,
    long SizeBytes,
    int? DurationMs = null);
