using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shoyee.Desktop.Theming;

namespace Shoyee.Desktop.ViewModels.Settings;

/// <summary>
/// 设置页「模型配置」：上面是三个场景各用哪个模型，下面是接入的模型列表。
///
/// 列表是唯一的真源，场景只存模型 Id；保存时按场景展开成 daemon 的
/// <c>models.main / small / vision</c>（同一模型可重复出现在多个档位）。
/// 新增与编辑走弹窗（<see cref="Editing"/>），列表里不展开表单。
/// </summary>
public partial class ModelCatalogViewModel : ObservableObject
{
    private bool _loading;

    /// <summary>模型列表或场景绑定发生变化（由主窗口落盘）。</summary>
    public event EventHandler? Changed;

    /// <summary>创建模型配置页。</summary>
    public ModelCatalogViewModel()
    {
        Entries.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(HasEntries));
        };
    }

    /// <summary>接入的模型列表。</summary>
    public ObservableCollection<ModelEntryViewModel> Entries { get; } = [];

    /// <summary>主模型可选条目（勾了「主模型」场景的模型）。</summary>
    public ObservableCollection<ModelEntryViewModel> ChatOptions { get; } = [];

    /// <summary>小模型可选条目。</summary>
    public ObservableCollection<ModelEntryViewModel> SmallOptions { get; } = [];

    /// <summary>视觉模型可选条目。</summary>
    public ObservableCollection<ModelEntryViewModel> VisionOptions { get; } = [];

    /// <summary>主模型（对话与工具调用）。</summary>
    [ObservableProperty]
    public partial ModelEntryViewModel? ChatEntry { get; set; }

    /// <summary>小模型（压缩摘要、会话标题）。</summary>
    [ObservableProperty]
    public partial ModelEntryViewModel? SmallEntry { get; set; }

    /// <summary>视觉模型（可空 = 不做图片分析）。</summary>
    [ObservableProperty]
    public partial ModelEntryViewModel? VisionEntry { get; set; }

    /// <summary>正在弹窗编辑的模型；空 = 弹窗关闭。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDialogOpen))]
    [NotifyPropertyChangedFor(nameof(DialogTitle))]
    public partial ModelEntryViewModel? Editing { get; set; }

    /// <summary>弹窗是否打开。</summary>
    public bool IsDialogOpen => Editing is not null;

    /// <summary>弹窗标题。</summary>
    public string DialogTitle => Editing?.IsNew == true ? "添加模型" : "编辑模型";

    /// <summary>主模型未绑定时提醒。</summary>
    public bool IsChatMissing => ChatEntry is null;

    /// <summary>小模型未绑定时提醒。</summary>
    public bool IsSmallMissing => SmallEntry is null;

    /// <summary>列表是否为空。</summary>
    public bool IsEmpty => Entries.Count == 0;

    /// <summary>列表是否有内容。</summary>
    public bool HasEntries => Entries.Count > 0;

    /// <summary>把持久化配置灌进列表与场景绑定。</summary>
    /// <param name="settings">模型配置；空 = 默认配置。</param>
    public void Apply(ModelCatalogSettings? settings)
    {
        var catalog = settings ?? ModelCatalogSettings.Default;
        _loading = true;
        try
        {
            foreach (var entry in Entries)
            {
                entry.PropertyChanged -= OnEntryPropertyChanged;
            }

            Entries.Clear();
            foreach (var entry in catalog.Entries)
            {
                AddEntry(ModelEntryViewModel.FromSettings(entry));
            }

            RefreshOptions();
            ChatEntry = Entries.FirstOrDefault(entry => entry.Id == catalog.ChatEntryId);
            SmallEntry = Entries.FirstOrDefault(entry => entry.Id == catalog.SmallEntryId);
            VisionEntry = Entries.FirstOrDefault(entry => entry.Id == catalog.VisionEntryId);
        }
        finally
        {
            _loading = false;
        }

        RaiseMissing();
    }

    /// <summary>导出成持久化配置。</summary>
    /// <returns>模型列表 + 场景绑定。</returns>
    public ModelCatalogSettings ToSettings() => new(
        Entries.Select(entry => entry.ToSettings()).ToList(),
        ChatEntry?.Id,
        SmallEntry?.Id,
        VisionEntry?.Id);

    /// <summary>新增：打开空白弹窗，默认勾「主模型」。</summary>
    [RelayCommand]
    private void Add()
    {
        Editing = new ModelEntryViewModel(
            Guid.NewGuid().ToString("N"),
            string.Empty,
            "https://api.deepseek.com",
            "deepseek-flash",
            "DEEPSEEK_API_KEY",
            1_000_000,
            [ModelScene.Chat],
            isNew: true);
    }

    /// <summary>编辑：打开填好当前值的弹窗。</summary>
    /// <param name="entry">要编辑的模型。</param>
    [RelayCommand]
    private void Edit(ModelEntryViewModel? entry)
    {
        if (entry is not null)
        {
            Editing = entry.CloneForEdit();
        }
    }

    /// <summary>弹窗保存：校验后写回列表，并清掉不再支持的场景绑定。</summary>
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
        }
        else if (Entries.FirstOrDefault(entry => entry.Id == Editing.Id) is { } target)
        {
            target.ApplyFrom(Editing);
        }

        Editing = null;
        RefreshOptions();
        RaiseMissing();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>关闭弹窗，丢弃未保存的改动。</summary>
    [RelayCommand]
    private void CancelDialog() => Editing = null;

    /// <summary>清掉视觉模型绑定（= 不做图片分析）。</summary>
    [RelayCommand]
    private void ClearVision() => VisionEntry = null;

    /// <summary>删除一条模型，并清掉指向它的场景绑定。</summary>
    /// <param name="entry">要删除的模型。</param>
    [RelayCommand]
    private void Delete(ModelEntryViewModel? entry)
    {
        if (entry is null)
        {
            return;
        }

        if (ChatEntry == entry)
        {
            ChatEntry = null;
        }

        if (SmallEntry == entry)
        {
            SmallEntry = null;
        }

        if (VisionEntry == entry)
        {
            VisionEntry = null;
        }

        entry.PropertyChanged -= OnEntryPropertyChanged;
        Entries.Remove(entry);
        RefreshOptions();
        RaiseMissing();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void AddEntry(ModelEntryViewModel entry, bool insertAtStart = false)
    {
        entry.Owner = this;
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
        if (e.PropertyName is nameof(ModelEntryViewModel.IsChat)
            or nameof(ModelEntryViewModel.IsSmall)
            or nameof(ModelEntryViewModel.IsVision))
        {
            RefreshOptions();
            RaiseMissing();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void RefreshOptions()
    {
        Sync(ChatOptions, Entries.Where(entry => entry.IsChat));
        Sync(SmallOptions, Entries.Where(entry => entry.IsSmall));
        Sync(VisionOptions, Entries.Where(entry => entry.IsVision));

        if (ChatEntry is not null && !ChatOptions.Contains(ChatEntry))
        {
            ChatEntry = null;
        }

        if (SmallEntry is not null && !SmallOptions.Contains(SmallEntry))
        {
            SmallEntry = null;
        }

        if (VisionEntry is not null && !VisionOptions.Contains(VisionEntry))
        {
            VisionEntry = null;
        }
    }

    /// <summary>
    /// 增量同步下拉选项。不能整表 Clear：下拉看到列表被清空会把 SelectedItem 写回 null，
    /// 把用户在场景里的选择冲掉。
    /// </summary>
    private static void Sync(
        ObservableCollection<ModelEntryViewModel> target,
        IEnumerable<ModelEntryViewModel> source)
    {
        var desired = source.ToList();

        for (var i = target.Count - 1; i >= 0; i--)
        {
            if (!desired.Contains(target[i]))
            {
                target.RemoveAt(i);
            }
        }

        for (var i = 0; i < desired.Count; i++)
        {
            if (i >= target.Count)
            {
                target.Add(desired[i]);
                continue;
            }

            if (ReferenceEquals(target[i], desired[i]))
            {
                continue;
            }

            var existing = target.IndexOf(desired[i]);
            if (existing >= 0)
            {
                target.Move(existing, i);
            }
            else
            {
                target.Insert(i, desired[i]);
            }
        }
    }

    private void RaiseMissing()
    {
        OnPropertyChanged(nameof(IsChatMissing));
        OnPropertyChanged(nameof(IsSmallMissing));
    }

    partial void OnChatEntryChanged(ModelEntryViewModel? value)
    {
        OnPropertyChanged(nameof(IsChatMissing));
        NotifyChanged();
    }

    partial void OnSmallEntryChanged(ModelEntryViewModel? value)
    {
        OnPropertyChanged(nameof(IsSmallMissing));
        NotifyChanged();
    }

    partial void OnVisionEntryChanged(ModelEntryViewModel? value) => NotifyChanged();

    private void NotifyChanged()
    {
        if (!_loading)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
