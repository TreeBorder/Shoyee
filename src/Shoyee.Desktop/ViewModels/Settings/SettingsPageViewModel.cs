using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Shoyee.Desktop.ViewModels.Settings;

/// <summary>
/// 设置页左侧二级菜单的一项。
/// 数据来自 <see cref="SettingsPageItem"/>，额外带一个「当前是否选中」的界面状态；
/// 选中动作交给 <see cref="MainViewModel"/>，菜单项自己不改导航。
/// </summary>
public sealed partial class SettingsPageViewModel : ObservableObject
{
    private readonly Action<SettingsPageViewModel> _select;

    /// <summary>创建子页菜单项。</summary>
    /// <param name="definition">页面定义。</param>
    /// <param name="select">点击时回调。</param>
    public SettingsPageViewModel(SettingsPageItem definition, Action<SettingsPageViewModel> select)
    {
        Definition = definition;
        _select = select;
        Groups = definition.Groups;
        SelectCommand = new RelayCommand(() => _select(this));
    }

    /// <summary>页面定义。</summary>
    public SettingsPageItem Definition { get; }

    /// <summary>子页键。</summary>
    public string Key => Definition.Key;

    /// <summary>子页标题（左侧二级菜单文案）。</summary>
    public string Title => Definition.Title;

    /// <summary>子页说明。</summary>
    public string Summary => Definition.Summary;

    /// <summary>数据来源。</summary>
    public string DataSource => Definition.DataSource;

    /// <summary>完成度。</summary>
    public SettingsPageStatus Status => Definition.Status;

    /// <summary>接入前还差什么。</summary>
    public IReadOnlyList<string> Planned => Definition.Planned;

    /// <summary>结构化表单；占位页用目录里的样例，接上运行配置后换成实时值。</summary>
    [ObservableProperty]
    public partial IReadOnlyList<SettingsGroupItem>? Groups { get; set; }

    /// <summary>表单是否已经是 daemon 的实时值（只读）。</summary>
    [ObservableProperty]
    public partial bool IsLiveData { get; set; }

    /// <summary>这一页是否处于编辑态。</summary>
    [ObservableProperty]
    public partial bool IsEditing { get; set; }

    /// <summary>进入编辑态（只有带 ConfigPath 的字段可改）。</summary>
    public void BeginEdit()
    {
        if (!IsLiveData)
        {
            return;
        }

        IsEditing = true;
        foreach (var field in Groups?.SelectMany(group => group.Fields) ?? [])
        {
            field.BeginEdit();
        }
    }

    /// <summary>取消编辑并丢弃未保存的值。</summary>
    public void CancelEdit()
    {
        IsEditing = false;
        foreach (var field in Groups?.SelectMany(group => group.Fields) ?? [])
        {
            field.CancelEdit();
        }
    }

    partial void OnIsEditingChanged(bool value)
    {
        if (!value)
        {
            foreach (var field in Groups?.SelectMany(group => group.Fields) ?? [])
            {
                field.IsEditing = false;
            }
        }
    }

    /// <summary>当前是否选中的子页。</summary>
    [ObservableProperty]
    public partial bool IsActive { get; set; }

    /// <summary>点击这一项。</summary>
    public IRelayCommand SelectCommand { get; }
}
