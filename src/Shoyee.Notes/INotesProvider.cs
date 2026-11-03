namespace Shoyee.Notes;

/// <summary>
/// 一个笔记源（Obsidian vault、将来的 Notion / Joplin 等）。
///
/// 核心只定**所有实现共有**的能力：身份 + 是否可用 + 检索 + 读取。定位用 <see cref="NoteRef"/>，
/// 不假设命中是一个本地文件——没有本地路径的后端（Notion 之类）照样能读。
///
/// 其余能力各自独立成接口，用 <c>is</c> 探测，不塞进这里：
/// <see cref="INotesWriter"/>（远端写）、<see cref="ILocalNotesFiles"/>（本地路径）。
/// 加能力时只加接口，不改这个契约，现有实现不受影响。
/// </summary>
public interface INotesProvider
{
    /// <summary>provider key（与 <c>notes.provider</c> / <c>notes.providers.{key}</c> 一致，诊断与多源展示用）。</summary>
    string Name { get; }

    /// <summary>是否配置好了可用的笔记源（未配置 / 目录不存在时为 false，笔记工具不挂载）。</summary>
    bool Configured { get; }

    /// <summary>检索笔记，返回定位 + 命中片段（最多 <paramref name="take"/> 条）。</summary>
    /// <param name="query">检索词（按字面匹配，不解析查询语法）。</param>
    /// <param name="take">最多返回条数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<IReadOnlyList<NoteHit>> SearchAsync(
        string query,
        int take = 10,
        CancellationToken cancellationToken = default);

    /// <summary>按定位读正文；笔记不存在 / 无权访问时返回 null。</summary>
    /// <param name="note">检索结果里的定位（Id 对实现方是不透明字符串）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<NoteDocument?> ReadAsync(NoteRef note, CancellationToken cancellationToken = default);
}
