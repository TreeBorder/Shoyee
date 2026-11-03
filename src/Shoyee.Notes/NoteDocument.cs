namespace Shoyee.Notes;

/// <summary>
/// 一篇笔记的正文。
/// </summary>
/// <param name="Ref">定位（原样带回）。</param>
/// <param name="Title">标题（可空；后端没有标题概念时为 null）。</param>
/// <param name="Content">正文（Markdown / 纯文本；超长时按实现方上限截断）。</param>
/// <param name="Truncated">是否因超长截断。</param>
/// <param name="ModifiedAt">最后修改时间（后端给得出时才填）。</param>
public sealed record NoteDocument(
    NoteRef Ref,
    string? Title,
    string Content,
    bool Truncated = false,
    DateTimeOffset? ModifiedAt = null);
