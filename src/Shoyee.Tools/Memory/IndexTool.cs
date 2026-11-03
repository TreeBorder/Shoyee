using System.ComponentModel;
using Microsoft.Extensions.AI;
using Shoyee.Contracts.Dtos;
using Shoyee.Memory.Index;
using Shoyee.Tools.Composition;

namespace Shoyee.Tools.Memory;

/// <summary>
/// 资料索引工具族 <c>index_list</c> / <c>index_add</c> / <c>index_update</c> / <c>index_remove</c>。
/// 条目存数据库；会话开局快照里的目录可能滞后，增删改后以 <c>index_list</c> 为准。
/// 写库不挂审批（与工作区文件写入同级的低风险操作）。
/// </summary>
public static class IndexTool
{
    /// <summary>读取目录工具名。</summary>
    public const string ListToolName = "index_list";

    /// <summary>新增工具名。</summary>
    public const string AddToolName = "index_add";

    /// <summary>修改工具名。</summary>
    public const string UpdateToolName = "index_update";

    /// <summary>删除工具名。</summary>
    public const string RemoveToolName = "index_remove";

    /// <summary>注释软上限（注入体积纪律）。</summary>
    private const int CommentSoftCap = 60;

    /// <summary>构建四个索引工具。</summary>
    public static IReadOnlyList<ToolDescriptor> Create(IIndexStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        var list = AIFunctionFactory.Create(
            async (
                [Description("范围：all / work / roleplay；省略 = all。")] string? scope = null,
                [Description("关键词过滤（键/标题/地址/备注）；省略 = 不过滤。")] string? query = null,
                CancellationToken cancellationToken = default) =>
            {
                var entries = await store.ListAsync(NormalizeScope(scope), query, enabledOnly: true, cancellationToken)
                    .ConfigureAwait(false);
                if (entries.Count == 0)
                {
                    return "索引是空的（没有匹配条目）。新增用 index_add。";
                }

                return $"# 资料索引（{entries.Count} 条）\n"
                       + string.Join(
                           "\n",
                           entries.Select(static e =>
                               $"- [{e.Scope}] {e.Key}｜{e.Title}（{e.Kind}）｜{e.Value}"
                               + (string.IsNullOrWhiteSpace(e.Notes) ? string.Empty : $"｜{e.Notes}")))
                       + "\n\n细节按地址自己读原文（file_access_* / notes_search）；本列表是库里的当前内容。";
            },
            new AIFunctionFactoryOptions
            {
                Name = ListToolName,
                Description = "读取资料索引的当前目录（键｜标题｜类型｜地址｜备注）。会话开局快照可能滞后——增删改之后、或想确认改动是否生效，用本工具重新读。",
            });

        var add = AIFunctionFactory.Create(
            async (
                [Description("稳定键（唯一；更新/删除都按它定位）。")] string key,
                [Description("展示名。")] string title,
                [Description("类型：url / path / note / contact / misc。")] string kind,
                [Description("地址或值（可直接读取的绝对路径 / vault 相对路径 / URL）。")] string value,
                [Description("短注释（会进开局快照，≤60 字为宜）。")] string? notes = null,
                [Description("范围：all / work / roleplay（默认 all）。")] string? scope = null,
                CancellationToken cancellationToken = default) =>
            {
                var k = (key ?? string.Empty).Trim();
                var t = (title ?? string.Empty).Trim();
                var v = (value ?? string.Empty).Trim();
                if (k.Length == 0 || t.Length == 0 || v.Length == 0)
                {
                    return "key / title / value 三个都要填。";
                }

                if (await store.FindAsync(k, cancellationToken).ConfigureAwait(false) is not null)
                {
                    return $"已有键「{k}」——改它用 {UpdateToolName}，删掉重建用 {RemoveToolName}。";
                }

                var entry = new IndexEntry(
                    0,
                    k,
                    t,
                    string.IsNullOrWhiteSpace(kind) ? "misc" : kind.Trim(),
                    v,
                    NormalizeScope(scope),
                    null,
                    string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
                    false,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow);
                await store.UpsertAsync(entry, cancellationToken).ConfigureAwait(false);

                var warn = notes is { Length: > CommentSoftCap }
                    ? $"（备注 {notes.Length} 字超过 {CommentSoftCap} 字软上限，注入体积会涨，建议精简）"
                    : string.Empty;
                return $"已添加「{k}」{warn}。本会话开局快照不会跟着变，以 {ListToolName} 为准。";
            },
            new AIFunctionFactoryOptions
            {
                Name = AddToolName,
                Description = "往资料索引新增一条（key/title/kind/value 必填；key 唯一）。写库，免审批。",
            });

        var update = AIFunctionFactory.Create(
            async (
                [Description("要改的键（index_list 里的 key，大小写不敏感）。")] string key,
                [Description("新标题；不改就省略。")] string? title = null,
                [Description("新类型；不改就省略。")] string? kind = null,
                [Description("新地址；不改就省略。")] string? value = null,
                [Description("新备注；不改就省略。")] string? notes = null,
                [Description("新范围（all / work / roleplay）；不改就省略。")] string? scope = null,
                CancellationToken cancellationToken = default) =>
            {
                var k = (key ?? string.Empty).Trim();
                if (k.Length == 0)
                {
                    return "要给 key 才知道改哪条（index_list 看现有键）。";
                }

                if (title is null && kind is null && value is null && notes is null && scope is null)
                {
                    return "至少给 title / kind / value / notes / scope 之一。";
                }

                var existing = await store.FindAsync(k, cancellationToken).ConfigureAwait(false);
                if (existing is null)
                {
                    return $"没有键为「{k}」的条目；用 {ListToolName} 看现有键。";
                }

                var updated = existing with
                {
                    Title = string.IsNullOrWhiteSpace(title) ? existing.Title : title.Trim(),
                    Kind = string.IsNullOrWhiteSpace(kind) ? existing.Kind : kind.Trim(),
                    Value = string.IsNullOrWhiteSpace(value) ? existing.Value : value.Trim(),
                    Notes = string.IsNullOrWhiteSpace(notes) ? existing.Notes : notes.Trim(),
                    Scope = NormalizeScope(scope, existing.Scope),
                };
                await store.UpsertAsync(updated, cancellationToken).ConfigureAwait(false);

                var warn = updated.Notes is { Length: > CommentSoftCap }
                    ? $"（备注 {updated.Notes.Length} 字超过 {CommentSoftCap} 字软上限，建议精简）"
                    : string.Empty;
                return $"已更新「{updated.Key}」{warn}。以 {ListToolName} 为准。";
            },
            new AIFunctionFactoryOptions
            {
                Name = UpdateToolName,
                Description = "按键修改资料索引条目（至少改一项；key 本身不可改，要改键就 remove 再 add）。写库，免审批。",
            });

        var remove = AIFunctionFactory.Create(
            async ([Description("要删的键（index_list 里的 key，大小写不敏感）。")] string key, CancellationToken cancellationToken = default) =>
            {
                var k = (key ?? string.Empty).Trim();
                if (k.Length == 0)
                {
                    return "要给 key 才知道删哪条（index_list 看现有键）。";
                }

                var existing = await store.FindAsync(k, cancellationToken).ConfigureAwait(false);
                if (existing is null)
                {
                    return $"没有键为「{k}」的条目；用 {ListToolName} 看现有键。";
                }

                await store.RemoveAsync(existing.Key, cancellationToken).ConfigureAwait(false);
                return $"已删除「{existing.Key}」。以 {ListToolName} 为准。";
            },
            new AIFunctionFactoryOptions
            {
                Name = RemoveToolName,
                Description = "按键删除资料索引条目。写库，免审批。",
            });

        return
        [
            ToolDescriptor.Create(list, ToolScope.All, writes: false, requiresApproval: false),
            ToolDescriptor.Create(add, ToolScope.Work, writes: true, requiresApproval: false),
            ToolDescriptor.Create(update, ToolScope.Work, writes: true, requiresApproval: false),
            ToolDescriptor.Create(remove, ToolScope.Work, writes: true, requiresApproval: false),
        ];
    }

    private static string NormalizeScope(string? scope, string fallback = "all")
        => scope?.Trim().ToLowerInvariant() switch
        {
            "work" => "work",
            "roleplay" or "rp" => "roleplay",
            "all" => "all",
            _ => fallback,
        };
}
