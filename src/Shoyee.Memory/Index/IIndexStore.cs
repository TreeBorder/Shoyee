using Shoyee.Contracts.Dtos;

namespace Shoyee.Memory.Index;

/// <summary>INDEX 地址簿的对外能力（工具面 <c>index_*</c>、Gateway 的 <c>index.*</c> 共用）。</summary>
public interface IIndexStore
{
    /// <summary>列出条目（scope 为 all/work/roleplay；all = 全部；<paramref name="enabledOnly"/> = 只列启用的）。</summary>
    Task<IReadOnlyList<IndexEntry>> ListAsync(
        string scope = "all",
        string? query = null,
        bool enabledOnly = false,
        CancellationToken cancellationToken = default);

    /// <summary>按 key 找条目（大小写不敏感）；没有返回 null。</summary>
    Task<IndexEntry?> FindAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>新增或按 key 覆盖。</summary>
    Task<IndexEntry> UpsertAsync(IndexEntry entry, CancellationToken cancellationToken = default);

    /// <summary>按 key 删除；返回是否删到。</summary>
    Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>渲染某模式的开局目录（每条一行）；没有条目时返回 null。</summary>
    Task<string?> RenderDirectoryAsync(string scope, CancellationToken cancellationToken = default);
}
