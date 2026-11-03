using Shoyee.Contracts.Dtos;

namespace Shoyee.Storage.Abstractions;

/// <summary>INDEX 地址簿读写。</summary>
public interface IIndexRepository
{
    /// <summary>列出条目（可按 scope / 关键词过滤；<paramref name="enabledOnly"/> = 只返回启用的）。</summary>
    Task<IReadOnlyList<IndexEntry>> ListAsync(
        string? scope = null,
        string? query = null,
        bool enabledOnly = false,
        CancellationToken cancellationToken = default);

    /// <summary>新增或按 key 覆盖。</summary>
    Task<IndexEntry> UpsertAsync(IndexEntry entry, CancellationToken cancellationToken = default);

    /// <summary>按 key 删除；返回是否删到。</summary>
    Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default);
}
