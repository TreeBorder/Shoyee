using CommunityToolkit.Mvvm.ComponentModel;
using Shoyee.Contracts.Dtos;

namespace Shoyee.Desktop.ViewModels.Settings;

/// <summary>资料索引的一行（来自 index.list；启停 / 删除写回 index.upsert / index.remove）。</summary>
public sealed partial class IndexEntryItem : ObservableObject
{
    /// <summary>从 RPC 条目投影。</summary>
    /// <param name="entry">索引条目。</param>
    public IndexEntryItem(IndexEntry entry)
    {
        Source = entry;
        Key = entry.Key;
        Name = string.IsNullOrWhiteSpace(entry.Title) ? entry.Key : entry.Title;
        Comment = entry.Notes ?? (entry.Tags is { Count: > 0 } tags ? string.Join(" · ", tags) : "");
        Address = entry.Value;
        Scope = entry.Scope;
        IsEnabled = entry.Enabled;
    }

    /// <summary>原始条目（启停时带上其余字段回写）。</summary>
    public IndexEntry Source { get; private set; }

    /// <summary>稳定键。</summary>
    public string Key { get; }

    /// <summary>名称。</summary>
    public string Name { get; }

    /// <summary>注释 / 标签。</summary>
    public string Comment { get; }

    /// <summary>地址或值。</summary>
    public string Address { get; }

    /// <summary>可见范围。</summary>
    public string Scope { get; }

    /// <summary>是否启用。</summary>
    [ObservableProperty]
    public partial bool IsEnabled { get; set; }

    /// <summary>范围的中文标签。</summary>
    public string ScopeLabel => Scope switch
    {
        "work" => "工作",
        "roleplay" => "角色",
        _ => "通用",
    };

    /// <summary>启用状态切换后用服务端返回的条目更新本行。</summary>
    /// <param name="entry">更新后的条目。</param>
    public void Update(IndexEntry entry)
    {
        Source = entry;
        IsEnabled = entry.Enabled;
    }
}
