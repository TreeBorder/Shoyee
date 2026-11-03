using Shoyee.Contracts.Dtos;
using Shoyee.Storage.Abstractions;

namespace Shoyee.Memory.History;

/// <summary><see cref="ISessionSearch"/> 的默认实现：检索走 <see cref="ISearchRepository"/>，阅读走消息投影。</summary>
public sealed class SessionSearch : ISessionSearch
{
    private readonly ISearchRepository _search;
    private readonly IMessageRepository _messages;

    /// <summary>创建检索服务。</summary>
    /// <param name="search">全文检索仓库（FTS5 + LIKE 兜底）。</param>
    /// <param name="messages">消息投影仓库（阅读用）。</param>
    public SessionSearch(ISearchRepository search, IMessageRepository messages)
    {
        ArgumentNullException.ThrowIfNull(search);
        ArgumentNullException.ThrowIfNull(messages);
        _search = search;
        _messages = messages;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SearchHit>> SearchAsync(
        string query,
        SessionSearchFilter? filter = null,
        CancellationToken cancellationToken = default)
        => _search.SearchSessionsAsync(query, filter ?? new SessionSearchFilter(), cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<MessageRecord>> ReadAsync(
        string sessionId,
        long? afterId = null,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        return _messages.GetPageAsync(
            sessionId: sessionId,
            afterId: afterId,
            limit: Math.Clamp(take, 1, 500),
            cancellationToken: cancellationToken);
    }
}
