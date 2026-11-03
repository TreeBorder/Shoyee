using System.ComponentModel;
using Microsoft.Extensions.AI;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;
using Shoyee.Memory.History;
using Shoyee.Tools.Composition;

namespace Shoyee.Tools.Memory;

/// <summary>
/// <c>session_search</c> / <c>session_read</c>：历史会话的「回忆」。
/// 检索走 <see cref="ISessionSearch"/>（FTS5 trigram + LIKE 兜底），不重写检索。
/// </summary>
public static class HistoryTool
{
    /// <summary>检索工具名。</summary>
    public const string SearchToolName = "session_search";

    /// <summary>转录阅读工具名。</summary>
    public const string ReadToolName = "session_read";

    /// <summary>构建两个检索工具。</summary>
    public static IReadOnlyList<ToolDescriptor> Create(ISessionSearch search)
    {
        ArgumentNullException.ThrowIfNull(search);

        var searchTool = AIFunctionFactory.Create(
            async (
                [Description("检索词（字面匹配，不用写查询语法）。")] string query,
                [Description("限定模式：work / roleplay；省略 = 两种都查。")] string? mode = null,
                [Description("限定 Lane id（可空）。")] string? laneId = null,
                [Description("最多返回几条命中（默认 10，上限 50）。")] int take = 10,
                CancellationToken cancellationToken = default) =>
            {
                var filter = new SessionSearchFilter(
                    Mode: ParseMode(mode),
                    LaneId: string.IsNullOrWhiteSpace(laneId) ? null : laneId,
                    Limit: Math.Clamp(take, 1, 50));
                var hits = await search.SearchAsync(query, filter, cancellationToken).ConfigureAwait(false);
                if (hits.Count == 0)
                {
                    return $"历史会话里没有命中「{query}」。";
                }

                return string.Join(
                           "\n\n",
                           hits.Select(static (h, i) =>
                               $"[{i + 1}] {h.SessionId ?? "(RP)"} · {h.SessionTitle ?? "(无标题)"} · {h.Mode} · {h.CreatedAt:yyyy-MM-dd HH:mm}\n  {h.Snippet}"))
                       + "\n\n深入阅读工作会话：session_read(session_id=…)；RP 历史用 lane.messages。";
            },
            new AIFunctionFactoryOptions
            {
                Name = SearchToolName,
                Description = "在历史会话里检索（回忆）：返回命中片段 + 会话引用（session_id）。想读整段上下文用 session_read。",
            });

        var readTool = AIFunctionFactory.Create(
            async (
                [Description("工作会话 id（session_search 返回的会话引用）。")] string sessionId,
                [Description("从这条消息 id 之后开始（不含）；省略 = 从头。")] long? afterId = null,
                [Description("最多拉多少条（默认 100）。")] int take = 100,
                CancellationToken cancellationToken = default) =>
            {
                var entries = await search.ReadAsync(sessionId, afterId, take, cancellationToken).ConfigureAwait(false);
                if (entries.Count == 0)
                {
                    return $"会话 {sessionId} 里没有更多消息（afterId={afterId}）。";
                }

                return $"# 会话 {sessionId}（{entries.Count} 条，最后 id={entries[^1].Id}）\n\n"
                       + string.Join(
                           "\n",
                           entries.Select(static e =>
                               $"{e.Role} [{e.CreatedAt:yyyy-MM-dd HH:mm}]{(e.ToolName is null ? string.Empty : $" [tool: {e.ToolName}]")}\n  {e.Content}"));
            },
            new AIFunctionFactoryOptions
            {
                Name = ReadToolName,
                Description = "拉取某个工作会话的转录（按消息 id 分页）：深入阅读 session_search 命中的上下文，或回忆某件事的来龙去脉。",
            });

        return
        [
            ToolDescriptor.Create(searchTool, ToolScope.All, writes: false, requiresApproval: false),
            ToolDescriptor.Create(readTool, ToolScope.All, writes: false, requiresApproval: false),
        ];
    }

    private static Mode? ParseMode(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            "work" => Mode.Work,
            "roleplay" or "rp" => Mode.Roleplay,
            _ => null,
        };
}
