using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Shoyee.Desktop.ViewModels.Settings;

/// <summary>
/// 设置页左侧一级菜单的一项：标题、图标、展开状态与二级子页。
/// 只有当前一级菜单会展开，其余收起，避免左栏被 30 个子页撑满。
/// </summary>
public sealed partial class SettingsSectionViewModel : ObservableObject
{
    /// <summary>创建一级菜单项。</summary>
    /// <param name="definition">一级菜单定义。</param>
    /// <param name="selectSection">点击一级菜单时回调（打开它记住的子页）。</param>
    /// <param name="selectPage">点击某个子页时回调。</param>
    public SettingsSectionViewModel(
        SettingsSectionItem definition,
        Action<SettingsSectionViewModel> selectSection,
        Action<SettingsPageViewModel> selectPage)
    {
        Definition = definition;
        Pages = definition.Pages
            .Select(page => new SettingsPageViewModel(page, selectPage))
            .ToList();
        SelectCommand = new RelayCommand(() => selectSection(this));
    }

    /// <summary>一级菜单定义。</summary>
    public SettingsSectionItem Definition { get; }

    /// <summary>对应的一级菜单枚举。</summary>
    public SettingsSection Section => Definition.Section;

    /// <summary>菜单文案。</summary>
    public string Title => Definition.Title;

    /// <summary>菜单图标。</summary>
    public Geometry Icon => Definition.Icon;

    /// <summary>一级菜单说明。</summary>
    public string Summary => Definition.Summary;

    /// <summary>二级子页。</summary>
    public IReadOnlyList<SettingsPageViewModel> Pages { get; }

    /// <summary>当前是否选中这个一级菜单。</summary>
    [ObservableProperty]
    public partial bool IsActive { get; set; }

    /// <summary>二级菜单是否展开（只有当前一级菜单为 true）。</summary>
    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    /// <summary>点击一级菜单。</summary>
    public IRelayCommand SelectCommand { get; }
}
