namespace Shoyee.Storage.Entities.Ops;

/// <summary>INDEX 地址簿条目。</summary>
public sealed class IndexEntryEntity
{
    /// <summary>自增 id。</summary>
    public long Id { get; set; }

    /// <summary>稳定键（唯一）。</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>展示名。</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>类型（url / path / note / contact / misc）。</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>地址或值。</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>可见范围（all / work / roleplay）。</summary>
    public string Scope { get; set; } = "all";

    /// <summary>标签（JSON 数组）。</summary>
    public string? TagsJson { get; set; }

    /// <summary>备注。</summary>
    public string? Notes { get; set; }

    /// <summary>是否置顶。</summary>
    public bool Pinned { get; set; }

    /// <summary>是否启用（停用后不进开局快照与工具视图）。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>创建时间（UTC）。</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>更新时间（UTC）。</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
