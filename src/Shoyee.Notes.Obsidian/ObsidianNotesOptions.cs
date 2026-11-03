namespace Shoyee.Notes.Obsidian;

/// <summary>Obsidian 笔记库配置（<c>notes.providers.obsidian</c> 节）。</summary>
/// <param name="VaultRoot">vault 根目录（绝对路径；相对路径按工作区根解析）。</param>
/// <param name="NotesDir">新笔记默认目录（相对 vault 根；不存在时由文件工具自动建）。</param>
public sealed record ObsidianNotesOptions(
    string VaultRoot = "",
    string NotesDir = "notes/shoyee");
