namespace Shoyee.Notes;

/// <summary>
/// 可选能力：能通过 API 写笔记的源（Notion / Joplin 等远端后端）。
///
/// **本地文件库故意不实现它**：本地写继续走 <c>file_access_*</c>（有 replace / replace_lines、
/// 受审批与路径围栏约束），多开一条绕过审批的写路径没有好处。只有「文件工具够不着」的后端才用它。
/// </summary>
public interface INotesWriter
{
    /// <summary>新建一篇笔记，返回它的定位。</summary>
    /// <param name="draft">标题、正文、目标目录（可空 = provider 默认位置）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<NoteRef> CreateAsync(NoteDraft draft, CancellationToken cancellationToken = default);

    /// <summary>在文末追加内容（目标不存在时由实现方决定是否报错）。</summary>
    /// <param name="note">目标定位。</param>
    /// <param name="markdown">要追加的正文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task AppendAsync(NoteRef note, string markdown, CancellationToken cancellationToken = default);

    /// <summary>整篇覆盖（远端后端的编辑入口；本地请用文件工具的精确替换）。</summary>
    /// <param name="note">目标定位。</param>
    /// <param name="markdown">新正文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task OverwriteAsync(NoteRef note, string markdown, CancellationToken cancellationToken = default);
}
