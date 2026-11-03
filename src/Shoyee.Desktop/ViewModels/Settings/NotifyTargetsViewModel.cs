using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Shoyee.Desktop.ViewModels.Settings;

/// <summary>
/// 设置页「通知」：简单列表，新增时先选供应商类型（系统 / ServerChan / 邮件），再填对应字段。
///
/// 后端只有**一个** <c>notify.provider</c>，所以列表里同一时间只允许一条「已启用」；
/// 打开一条会自动关掉其他条。接 daemon 后由 <c>config.set</c> 写 `notify.*`。
/// </summary>
public partial class NotifyTargetsViewModel : ObservableObject
{
    private bool _syncing;

    /// <summary>创建通知页，带三条示例目标。</summary>
    public NotifyTargetsViewModel()
    {
        AddEntry(new NotifyTargetViewModel(
            NotifyKind.System,
            "系统通知",
            fallbackToScript: false));
        AddEntry(new NotifyTargetViewModel(
            NotifyKind.ServerChan,
            "ServerChan 推送",
            isEnabled: false));
        AddEntry(new NotifyTargetViewModel(
            NotifyKind.Email,
            "邮件备份",
            isEnabled: false));
    }

    /// <summary>通知目标列表。</summary>
    public ObservableCollection<NotifyTargetViewModel> Entries { get; } = [];

    /// <summary>正在弹窗编辑的目标；空 = 弹窗关闭。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDialogOpen))]
    [NotifyPropertyChangedFor(nameof(DialogTitle))]
    public partial NotifyTargetViewModel? Editing { get; set; }

    /// <summary>弹窗是否打开。</summary>
    public bool IsDialogOpen => Editing is not null;

    /// <summary>弹窗标题。</summary>
    public string DialogTitle => Editing?.IsNew == true ? "添加通知" : "编辑通知";

    /// <summary>新增：打开空白弹窗，默认系统通知，可先切类型。</summary>
    [RelayCommand]
    private void Add()
    {
        Editing = new NotifyTargetViewModel(
            NotifyKind.System,
            string.Empty,
            isNew: true);
    }

    /// <summary>编辑：打开填好当前值的弹窗。</summary>
    /// <param name="entry">要编辑的目标。</param>
    [RelayCommand]
    private void Edit(NotifyTargetViewModel? entry)
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
            AddEntry(Editing, insertAtStart: true);
            if (Editing.IsEnabled)
            {
                MakeOnly(Editing);
            }
        }
        else if (Entries.FirstOrDefault(entry => entry.Id == Editing.Id) is { } target)
        {
            target.ApplyFrom(Editing);
            if (target.IsEnabled)
            {
                MakeOnly(target);
            }
        }

        Editing = null;
    }

    /// <summary>关闭弹窗，丢弃未保存的改动。</summary>
    [RelayCommand]
    private void CancelDialog() => Editing = null;

    /// <summary>删除一条通知目标；删掉正在启用的那条时，把第一条重新设为启用。</summary>
    /// <param name="entry">要删除的目标。</param>
    [RelayCommand]
    private void Delete(NotifyTargetViewModel? entry)
    {
        if (entry is null)
        {
            return;
        }

        var wasEnabled = entry.IsEnabled;
        entry.PropertyChanged -= OnEntryPropertyChanged;
        Entries.Remove(entry);

        if (wasEnabled && Entries.FirstOrDefault() is { } fallback)
        {
            fallback.IsEnabled = true;
        }
    }

    private void AddEntry(NotifyTargetViewModel entry, bool insertAtStart = false)
    {
        entry.PropertyChanged += OnEntryPropertyChanged;
        if (insertAtStart)
        {
            Entries.Insert(0, entry);
        }
        else
        {
            Entries.Add(entry);
        }
    }

    private void OnEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_syncing
            || e.PropertyName != nameof(NotifyTargetViewModel.IsEnabled)
            || sender is not NotifyTargetViewModel { IsEnabled: true } target)
        {
            return;
        }

        MakeOnly(target);
    }

    /// <summary>只保留一条启用。后端只有一个 notify.provider，不能同时推多条。</summary>
    private void MakeOnly(NotifyTargetViewModel target)
    {
        _syncing = true;
        try
        {
            foreach (var entry in Entries)
            {
                if (!ReferenceEquals(entry, target) && entry.IsEnabled)
                {
                    entry.IsEnabled = false;
                }
            }
        }
        finally
        {
            _syncing = false;
        }
    }
}
