namespace Shoyee.Notes;

/// <summary>
/// 笔记定位（不透明引用）：工具层只透传 <see cref="Id"/>，不解析。
/// </summary>
/// <param name="Provider">来源 provider 的 <see cref="INotesProvider.Name"/>（多源并存时区分）。</param>
/// <param name="Id">provider 自己的标识：本地文件库是相对根目录的路径，远端可能是 page id。</param>
/// <param name="Title">可读标题（可空；展示用，不参与定位）。</param>
/// <param name="Path">
/// 能直接交给文件工具的绝对路径（本地文件库填；远端后端没有本地路径时为 null，
/// 这时读正文只能用 <c>notes_read</c>）。
/// </param>
public sealed record NoteRef(string Provider, string Id, string? Title = null, string? Path = null);
