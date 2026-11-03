using Shoyee.Contracts.Dtos;
using Shoyee.Storage.Abstractions;

namespace Shoyee.Memory.History;

/// <summary>
/// 历史会话检索与阅读（工具面 <c>session_search</c> / <c>session_read</c>、Gateway 的
/// <c>session.search</c> 共用）。底层是消息投影的 FTS5 + LIKE 兜底，不在这里重写检索。
/// </summary>
public interface ISessionSearch
{
    /// <summary>按关键词检索消息，返回开窗片段与所属会话/Lane。</summary>
    /// <param name="query">检索词（字面匹配，由实现方负责引号转义）。</param>
    /// <param name="filter">模式 / Lane / 条数过滤；null = 全库、默认条数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<IReadOnlyList<SearchHit>> SearchAsync(
        string query,
        SessionSearchFilter? filter = null,
        CancellationToken cancellationToken = default);

    /// <summary>按 id 游标顺序读某会话的消息投影（深入阅读命中上下文）。</summary>
    /// <param name="sessionId">工作会话 id。</param>
    /// <param name="afterId">从这条消息之后开始（不含）；null = 从头。</param>
    /// <param name="take">最多取几条。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<IReadOnlyList<MessageRecord>> ReadAsync(
        string sessionId,
        long? afterId = null,
        int take = 100,
        CancellationToken cancellationToken = default);
}
