using Avalonia.Controls;

namespace Shoyee.Desktop.Views;

/// <summary>
/// 设置页：左侧一级菜单 + 内容区子页签。
/// 目录见 <c>docs/设置页信息架构-v2.md</c>，页面清单在 <see cref="ViewModels.Settings.SettingsCatalog"/>。
/// </summary>
public partial class SettingsView : UserControl
{
    /// <summary>创建视图。</summary>
    public SettingsView()
    {
        InitializeComponent();
    }
}
