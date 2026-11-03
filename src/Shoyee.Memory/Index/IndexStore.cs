using Shoyee.Contracts.Dtos;
using Shoyee.Storage.Abstractions;

namespace Shoyee.Memory.Index;

/// <summary>
/// INDEX 地址簿的读写入口（工具面 <c>index_*</c>、Gateway 的 <c>index.*</c> 共用）。
/// 条目存数据库；<see cref="RenderDirectoryAsync"/> 渲染开局快照里的「名称｜类型｜地址」目录。
/// </summary>
public sealed class IndexStore : IIndexStore
{
    private readonly IIndexRepository _repository;

    /// <summary>创建索引访问。</summary>
    /// <param name="repository">INDEX 仓库。</param>
    public IndexStore(IIndexRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
    }

    /// <summary>列出条目（scope 为 all/work/roleplay；all = 全部）。</summary>
    public Task<IReadOnlyList<IndexEntry>> ListAsync(
        string scope = "all",
        string? query = null,
        bool enabledOnly = false,
        CancellationToken cancellationToken = default)
        => _repository.ListAsync(scope, query, enabledOnly, cancellationToken);

    /// <summary>新增或按 key 覆盖。</summary>
    public Task<IndexEntry> UpsertAsync(IndexEntry entry, CancellationToken cancellationToken = default)
        => _repository.UpsertAsync(entry, cancellationToken);

    /// <summary>按 key 找条目（大小写不敏感）；没有返回 null。</summary>
    public async Task<IndexEntry?> FindAsync(string key, CancellationToken cancellationToken = default)
    {
        var entries = await _repository.ListAsync("all", cancellationToken: cancellationToken).ConfigureAwait(false);
        return entries.FirstOrDefault(entry => string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>按 key 删除；返回是否删到。</summary>
    public Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
        => _repository.RemoveAsync(key, cancellationToken);

    /// <summary>
    /// 渲染某模式的开局目录（每条一行）。没有条目时返回 null（调用方不注入该节）。
    /// </summary>
    public async Task<string?> RenderDirectoryAsync(string scope, CancellationToken cancellationToken = default)
    {
        var entries = await _repository.ListAsync(scope, enabledOnly: true, cancellationToken: cancellationToken).ConfigureAwait(false);
        return Render(entries);
    }

    /// <summary>把条目渲染成「- 标题（类型）｜地址｜备注」多行文本；空集返回 null。</summary>
    public static string? Render(IReadOnlyList<IndexEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count == 0)
        {
            return null;
        }

        return string.Join("\n", entries.Select(static entry =>
        {
            var notes = string.IsNullOrWhiteSpace(entry.Notes) ? string.Empty : $"｜{entry.Notes}";
            return $"- {entry.Title}（{entry.Kind}）｜{entry.Value}{notes}";
        }));
    }
}
