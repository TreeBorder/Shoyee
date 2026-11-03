namespace Shoyee.Notes;

/// <summary>一条命中：定位 + 命中处开窗的片段（不是全文）。</summary>
/// <param name="Ref">笔记定位（本地 provider 带绝对路径）。</param>
/// <param name="Snippet">片段（本地实现带行号前缀 <c>L12: …</c>，便于配合按行读取）。</param>
public sealed record NoteHit(NoteRef Ref, string Snippet);
