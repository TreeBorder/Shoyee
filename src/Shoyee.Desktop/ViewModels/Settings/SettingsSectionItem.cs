using Avalonia.Media;

namespace Shoyee.Desktop.ViewModels.Settings;

/// <summary>设置页的一个一级菜单：名称、图标与子页列表。</summary>
/// <param name="Section">对应的一级菜单枚举。</param>
/// <param name="Title">菜单文案。</param>
/// <param name="Summary">一级菜单的一句话说明，显示在内容区顶部。</param>
/// <param name="Icon">菜单图标。</param>
/// <param name="Pages">子页（至少一个）。</param>
public sealed record SettingsSectionItem(
    SettingsSection Section,
    string Title,
    string Summary,
    Geometry Icon,
    IReadOnlyList<SettingsPageItem> Pages);
