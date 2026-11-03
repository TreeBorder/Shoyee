using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Shoyee.Desktop.ViewModels.Settings;

/// <summary>
/// 设置页「渠道账号」：一个简单列表，新增时先选渠道类型，再填这类渠道的字段。
///
/// 现在只有 Mattermost 与微信两种；列表是本次运行内的占位数据，
/// 接 daemon 后换成 `channels.*` 的 config.get / config.set。
/// </summary>
public partial class ChannelAccountsViewModel : ObservableObject
{
    /// <summary>创建渠道账号页，带三条示例账号。</summary>
    public ChannelAccountsViewModel()
    {
        Entries.Add(new ChannelAccountViewModel(
            ChannelKind.Mattermost,
            "Mattermost · 角色",
            "https://chat.example.com",
            "SHOYEE_MM_RP_TOKEN",
            requireMention: false));
        Entries.Add(new ChannelAccountViewModel(
            ChannelKind.Mattermost,
            "Mattermost · 工作",
            "https://chat.example.com",
            "SHOYEE_MM_WORK_TOKEN"));
        Entries.Add(new ChannelAccountViewModel(
            ChannelKind.WeChat,
            "微信 · 工作",
            "https://ilinkai.weixin.qq.com",
            "SHOYEE_WECHAT_TOKEN"));
    }

    /// <summary>渠道账号列表。</summary>
    public ObservableCollection<ChannelAccountViewModel> Entries { get; } = [];

    /// <summary>正在弹窗编辑的账号；空 = 弹窗关闭。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDialogOpen))]
    [NotifyPropertyChangedFor(nameof(DialogTitle))]
    public partial ChannelAccountViewModel? Editing { get; set; }

    /// <summary>弹窗是否打开。</summary>
    public bool IsDialogOpen => Editing is not null;

    /// <summary>弹窗标题。</summary>
    public string DialogTitle => Editing?.IsNew == true ? "添加渠道" : "编辑渠道";

    /// <summary>新增：打开空白弹窗，默认 Mattermost，可先切类型。</summary>
    [RelayCommand]
    private void Add()
    {
        Editing = new ChannelAccountViewModel(
            ChannelKind.Mattermost,
            string.Empty,
            "https://chat.example.com",
            "SHOYEE_MM_TOKEN",
            isNew: true);
    }

    /// <summary>编辑：打开填好当前值的弹窗。</summary>
    /// <param name="entry">要编辑的账号。</param>
    [RelayCommand]
    private void Edit(ChannelAccountViewModel? entry)
    {
        if (entry is not null)
        {
            Editing = entry.CloneForEdit();
        }
    }

    /// <summary>弹窗保存。</summary>
    [RelayCommand]
    private void SaveDialog()
    {
        if (Editing is null || !Editing.Validate())
        {
            return;
        }

        if (Editing.IsNew)
        {
            Editing.IsNew = false;
            Entries.Insert(0, Editing);
        }
        else if (Entries.FirstOrDefault(entry => entry.Id == Editing.Id) is { } target)
        {
            target.ApplyFrom(Editing);
        }

        Editing = null;
    }

    /// <summary>关闭弹窗，丢弃未保存的改动。</summary>
    [RelayCommand]
    private void CancelDialog() => Editing = null;

    /// <summary>删除一个账号。</summary>
    /// <param name="entry">要删除的账号。</param>
    [RelayCommand]
    private void Delete(ChannelAccountViewModel? entry)
    {
        if (entry is not null)
        {
            Entries.Remove(entry);
        }
    }
}
