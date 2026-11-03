namespace Shoyee.Contracts.Dtos;

/// <summary>INDEX 条目（资源地址簿；存 DB，不再是 JSON 文件）。</summary>
/// <param name="Id">库内 id（新建时为 0）。</param>
/// <param name="Key">稳定键（唯一）。</param>
/// <param name="Title">展示名。</param>
/// <param name="Kind">类型：url / path / note / contact / misc。</param>
/// <param name="Value">地址或值。</param>
/// <param name="Scope">可见范围：all / work / roleplay。</param>
/// <param name="Tags">标签。</param>
/// <param name="Notes">备注。</param>
/// <param name="Pinned">是否置顶。</param>
/// <param name="CreatedAt">创建时间（UTC）。</param>
/// <param name="UpdatedAt">更新时间（UTC）。</param>
/// <param name="Enabled">是否启用；停用后仍保留定义与历史，但不进开局快照、模型工具也看不到。</param>
public sealed record IndexEntry(
    long Id,
    string Key,
    string Title,
    string Kind,
    string Value,
    string Scope,
    IReadOnlyList<string>? Tags,
    string? Notes,
    bool Pinned,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool Enabled = true);

/// <summary><c>index.list</c> 的请求。</summary>
/// <param name="Scope">限定范围（all / work / roleplay）；null = 不限。</param>
/// <param name="Query">关键词过滤；null = 不过滤。</param>
public sealed record IndexListRequest(string? Scope = null, string? Query = null);

/// <summary><c>index.list</c> 的返回。</summary>
/// <param name="Items">条目。</param>
public sealed record IndexListResult(IReadOnlyList<IndexEntry> Items);

/// <summary><c>index.upsert</c> 的请求。</summary>
/// <param name="Entry">要新增或按 key 覆盖的条目。</param>
public sealed record IndexUpsertRequest(IndexEntry Entry);

/// <summary><c>index.remove</c> 的请求。</summary>
/// <param name="Key">条目稳定键。</param>
public sealed record IndexRemoveRequest(string Key);
