namespace Shoyee.Notes;

/// <summary>新建笔记的输入。</summary>
/// <param name="Title">标题（远端后端多用来命名页面）。</param>
/// <param name="Markdown">正文。</param>
/// <param name="Folder">目标目录 / 父级（可空 = provider 默认位置）。</param>
public sealed record NoteDraft(string Title, string Markdown, string? Folder = null);
