using CommunityToolkit.Mvvm.ComponentModel;

namespace Shoyee.Desktop.ViewModels.Settings;

/// <summary>状态表里可编辑的一行（Key 是 state.set 的入参）。</summary>
public sealed partial class StateEditItem : ObservableObject
{
    /// <summary>创建一行。</summary>
    /// <param name="key">状态键。</param>
    /// <param name="name">展示名。</param>
    /// <param name="value">当前值。</param>
    /// <param name="description">说明。</param>
    public StateEditItem(string key, string name, string value, string? description)
    {
        Key = key;
        Name = name;
        OriginalValue = value;
        Value = value;
        Description = description ?? "";
    }

    /// <summary>状态键（如 mood / energy）。</summary>
    public string Key { get; }

    /// <summary>展示名。</summary>
    public string Name { get; }

    /// <summary>加载时的值（判断是否改过）。</summary>
    public string OriginalValue { get; private set; }

    /// <summary>说明。</summary>
    public string Description { get; }

    /// <summary>当前值（可编辑）。</summary>
    [ObservableProperty]
    public partial string Value { get; set; }

    /// <summary>是否改过。</summary>
    public bool IsDirty => !string.Equals(Value, OriginalValue, StringComparison.Ordinal);

    /// <summary>保存成功后把当前值设为新的基准。</summary>
    public void MarkSaved()
    {
        OriginalValue = Value;
        OnPropertyChanged(nameof(IsDirty));
    }

    partial void OnValueChanged(string value) => OnPropertyChanged(nameof(IsDirty));
}
