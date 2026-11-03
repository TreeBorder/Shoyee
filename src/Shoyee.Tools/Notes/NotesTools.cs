using System.ComponentModel;
using Microsoft.Extensions.AI;
using Shoyee.Notes;
using Shoyee.Tools.Composition;

namespace Shoyee.Tools.Notes;

/// <summary>
/// 笔记源工具面：<c>notes_search</c> / <c>notes_read</c> 必挂，<c>notes_write</c> 只在 provider
/// 实现 <see cref="INotesWriter"/> 时挂（本地文件库用 <c>file_access_*</c> 写，不重复开写路径）。
/// </summary>
public static class NotesTools
{
    /// <summary>检索工具名（MCP 防冒名保留名）。</summary>
    public const string SearchToolName = "notes_search";

    /// <summary>读取工具名。</summary>
    public const string ReadToolName = "notes_read";

    /// <summary>写入工具名（仅远端 provider）。</summary>
    public const string WriteToolName = "notes_write";

    /// <summary>构建笔记工具；未配置笔记源时返回空（不挂载）。</summary>
    public static IReadOnlyList<ToolDescriptor> Create(INotesProvider notes)
    {
        ArgumentNullException.ThrowIfNull(notes);
        if (!notes.Configured)
        {
            return [];
        }

        var root = (notes as ILocalNotesFiles)?.Root;
        var where = root is null ? "笔记源" : $"笔记源（{root}）";
        var editHint = root is null
            ? "要改内容用 notes_write。"
            : "要按行读 / 精确改，直接拿 path 用 file_access_read_lines / replace / replace_lines。";

        var search = AIFunctionFactory.Create(
            async (
                [Description("检索词（字面匹配，不用写查询语法）。")] string query,
                [Description("最多返回几条（默认 10，上限 50）。")] int take = 10,
                CancellationToken cancellationToken = default) =>
            {
                var hits = await notes.SearchAsync(query, take, cancellationToken).ConfigureAwait(false);
                if (hits.Count == 0)
                {
                    return $"笔记源里没有命中「{query}」。";
                }

                var lines = hits.Select(static h =>
                {
                    var header = $"- {h.Ref.Title ?? h.Ref.Id}（Id: {h.Ref.Id}）";
                    return h.Ref.Path is { } path
                        ? $"{header}\n  path: {path}\n  {h.Snippet}"
                        : $"{header}\n  {h.Snippet}";
                });
                return $"# 笔记命中（{hits.Count} 条）\n"
                       + string.Join("\n", lines)
                       + "\n\n读正文用 notes_read(id=...)；" + editHint;
            },
            new AIFunctionFactoryOptions
            {
                Name = SearchToolName,
                Description = $"在{where}里检索笔记：返回定位（Id + 本地绝对路径）+ 带行号的命中片段；正文用 notes_read 读。笔记源是外部资料，不会自动注入。",
            });

        var read = AIFunctionFactory.Create(
            async (
                [Description("笔记定位 Id（notes_search 返回的那个）。")] string id,
                CancellationToken cancellationToken = default) =>
            {
                var document = await notes
                    .ReadAsync(new NoteRef(notes.Name, id), cancellationToken)
                    .ConfigureAwait(false);
                if (document is null)
                {
                    return $"没找到这篇笔记：{id}（先 notes_search 确认 Id，别凭印象拼路径）。";
                }

                var header = $"# {document.Title ?? id}\n"
                             + $"Id: {document.Ref.Id}"
                             + (document.Ref.Path is { } path ? $"\npath: {path}" : string.Empty)
                             + (document.ModifiedAt is { } modified ? $"\n修改时间：{modified:yyyy-MM-dd HH:mm}" : string.Empty)
                             + (document.Truncated ? "\n（正文超长已截断）" : string.Empty);
                return $"{header}\n\n{document.Content}";
            },
            new AIFunctionFactoryOptions
            {
                Name = ReadToolName,
                Description = $"按 Id 读取{where}里一篇笔记的正文。" + editHint,
            });

        var tools = new List<ToolDescriptor>
        {
            ToolDescriptor.Create(search, ToolScope.All, writes: false, requiresApproval: false),
            ToolDescriptor.Create(read, ToolScope.All, writes: false, requiresApproval: false),
        };

        if (notes is INotesWriter writer)
        {
            tools.Add(CreateWriteTool(writer, notes.Name, where));
        }

        return tools;
    }

    /// <summary>远端 provider 的写入工具（本地文件库不走这里）。</summary>
    private static ToolDescriptor CreateWriteTool(INotesWriter writer, string providerName, string where)
    {
        var tool = AIFunctionFactory.Create(
            async (
                [Description("动作：create（新建）/ append（文末追加）/ overwrite（整篇覆盖）。")] string action,
                [Description("正文（Markdown）。")] string content,
                [Description("目标笔记 Id；create 时可空。")] string? id = null,
                [Description("标题；create 时必填。")] string? title = null,
                [Description("目标目录 / 父级；可空 = 默认位置。")] string? folder = null,
                CancellationToken cancellationToken = default) =>
            {
                var normalized = (action ?? string.Empty).Trim().ToLowerInvariant();
                switch (normalized)
                {
                    case "create":
                        if (string.IsNullOrWhiteSpace(title))
                        {
                            return "create 需要 title。";
                        }

                        var created = await writer
                            .CreateAsync(new NoteDraft(title!, content, folder), cancellationToken)
                            .ConfigureAwait(false);
                        return $"已新建：{created.Title ?? created.Id}（Id: {created.Id}）";

                    case "append":
                    case "overwrite":
                        if (string.IsNullOrWhiteSpace(id))
                        {
                            return $"{normalized} 需要 id。";
                        }

                        var target = new NoteRef(providerName, id!);
                        if (normalized == "append")
                        {
                            await writer.AppendAsync(target, content, cancellationToken).ConfigureAwait(false);
                        }
                        else
                        {
                            await writer.OverwriteAsync(target, content, cancellationToken).ConfigureAwait(false);
                        }

                        return normalized == "append" ? $"已追加：{id}" : $"已覆盖：{id}";

                    default:
                        return $"未知动作：{action}（可用 create / append / overwrite）。";
                }
            },
            new AIFunctionFactoryOptions
            {
                Name = WriteToolName,
                Description = $"在{where}里新建 / 追加 / 覆盖笔记。小改动优先 append；overwrite 会整篇替换，慎用。",
            });

        return ToolDescriptor.Create(tool, ToolScope.Work, writes: true, requiresApproval: true);
    }
}
