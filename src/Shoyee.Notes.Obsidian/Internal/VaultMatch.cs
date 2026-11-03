namespace Shoyee.Notes.Obsidian;

/// <summary>vault 检索的内部命中（相对 vault 根的路径 + 行号 + 片段），对外再包成 <see cref="NoteHit"/>。</summary>
/// <param name="Path">相对 vault 根的路径。</param>
/// <param name="Line">命中行号（从 1 起；取不到时为 null）。</param>
/// <param name="Snippet">命中处开窗的片段。</param>
internal sealed record VaultMatch(string Path, int? Line, string Snippet);
