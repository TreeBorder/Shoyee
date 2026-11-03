namespace Shoyee.Voice;

/// <summary>转写结果。</summary>
/// <param name="Text">识别出的文本（已 Trim，非空）。</param>
public sealed record TranscriptionResult(string Text);
