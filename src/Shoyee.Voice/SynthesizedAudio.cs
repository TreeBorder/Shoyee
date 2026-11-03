namespace Shoyee.Voice;

/// <summary>合成结果：音频字节 + 落盘要用的类型信息。</summary>
/// <param name="Data">音频字节（WAV/MP3 等，由 <see cref="Extension"/> 决定）。</param>
/// <param name="Mime">MIME 类型，如 <c>audio/wav</c>。</param>
/// <param name="Extension">扩展名（含点，小写），如 <c>.wav</c>。</param>
/// <param name="Transcript">供应商回带的文本（可空；只是参考，不是权威）。</param>
public sealed record SynthesizedAudio(
    byte[] Data,
    string Mime,
    string Extension,
    string? Transcript = null);
