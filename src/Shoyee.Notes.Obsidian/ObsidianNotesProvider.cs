using Shoyee.Contracts.Options;

namespace Shoyee.Notes.Obsidian;

/// <summary>
/// Obsidian vault 笔记源：vault 是 Markdown 笔记库（用途上是资料库，但没有索引/向量/RAG 语义）。
/// 实现 <see cref="ILocalNotesFiles"/>，读原文与写笔记走文件工具；检索走 <see cref="VaultSearch"/>
/// （ripgrep，缺失时托管扫描）。
/// 未配置 <c>notes.providers.obsidian.vaultRoot</c> 或目录不存在时 <see cref="Configured"/> 为 false，检索工具不挂载。
/// </summary>
public sealed class ObsidianNotesProvider : INotesProvider, ILocalNotesFiles
{
    /// <summary>provider key（与配置一致）。</summary>
    public const string Key = "obsidian";

    /// <summary>单篇读取上限（字符）；超过截断并标 <see cref="NoteDocument.Truncated"/>。</summary>
    private const int MaxReadChars = 200_000;

    private readonly string? _root;
    private readonly string? _notesDir;

    /// <summary>创建 vault 接入。</summary>
    /// <param name="options">Obsidian 配置节。</param>
    /// <param name="workspace">工作区配置（相对 vaultRoot 按工作区根解析）。</param>
    public ObsidianNotesProvider(ObsidianNotesOptions? options, WorkspaceConfig workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        if (string.IsNullOrWhiteSpace(options?.VaultRoot))
        {
            return;
        }

        var root = options!.VaultRoot.Trim();
        _root = Path.GetFullPath(
            Path.IsPathRooted(root) ? root : Path.Combine(workspace.Root, root));
        _notesDir = string.IsNullOrWhiteSpace(options.NotesDir)
            ? _root
            : Path.Combine(_root, options.NotesDir);
    }

    /// <summary>provider key。</summary>
    public string Name => Key;

    /// <summary>是否配置了 vault 且目录存在。</summary>
    public bool Configured => _root is not null && Directory.Exists(_root);

    /// <summary>vault 根目录（未配置时为 null）。</summary>
    public string? Root => _root;

    /// <summary>默认笔记目录（绝对路径；未配置时为 null）。</summary>
    public string? DefaultDirectory => _notesDir;

    /// <summary>检索 vault（字面匹配，返回相对路径定位 + 片段）。</summary>
    public async Task<IReadOnlyList<NoteHit>> SearchAsync(
        string query,
        int take = 10,
        CancellationToken cancellationToken = default)
    {
        if (_root is null)
        {
            return [];
        }

        var root = _root;
        var matches = await VaultSearch.SearchAsync(root, query, take, cancellationToken).ConfigureAwait(false);
        return [.. matches.Select(match => new NoteHit(
            new NoteRef(Key, match.Path, Path.GetFileNameWithoutExtension(match.Path), Path.Combine(root, match.Path)),
            match.Line is { } line ? $"L{line}: {match.Snippet}" : match.Snippet))];
    }

    /// <summary>按相对路径读正文；越过 vault 根、文件不存在、超长都按下面的规则处理。</summary>
    public async Task<NoteDocument?> ReadAsync(NoteRef note, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(note);
        if (_root is null || string.IsNullOrWhiteSpace(note.Id))
        {
            return null;
        }

        // Id 来自模型，可能被拼出 ../ 越界：解析后必须仍落在 vault 根内
        var root = RealRoot(_root);
        var full = Path.GetFullPath(Path.Combine(root, note.Id));
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            return null;
        }

        var info = new FileInfo(full);
        if (!info.Exists)
        {
            return null;
        }

        var content = await File.ReadAllTextAsync(full, cancellationToken).ConfigureAwait(false);
        var truncated = content.Length > MaxReadChars;
        if (truncated)
        {
            var cut = content.LastIndexOf('\n', MaxReadChars - 1) + 1;
            content = cut > 0 ? content[..cut] : content[..MaxReadChars];
        }

        return new NoteDocument(
            note with
            {
                Title = note.Title ?? Path.GetFileNameWithoutExtension(full),
                Path = full,
            },
            Path.GetFileNameWithoutExtension(full),
            content,
            truncated,
            info.LastWriteTimeUtc);
    }

    private static string RealRoot(string root)
        => Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
