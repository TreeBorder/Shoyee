using Shoyee.Contracts.Dtos;

namespace Shoyee.Storage.Abstractions;

/// <summary>全文检索（跨模式、跨 Lane）。</summary>
public interface ISearchRepository
{
    /// <summary>按关键词检索消息，返回片段命中。</summary>
    Task<IReadOnlyList<SearchHit>> SearchSessionsAsync(
        string query,
        SessionSearchFilter filter,
        CancellationToken cancellationToken = default);
}
