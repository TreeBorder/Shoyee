using CommunityToolkit.Mvvm.ComponentModel;

namespace Shoyee.Desktop.ViewModels.Settings;

/// <summary>
/// 设置页表单里的一个字段：标签、当前值、呈现方式。
/// 接上运行配置后 <see cref="ConfigPath"/> 指向 config.json 里的键，可编辑并按页保存。
/// </summary>
public sealed partial class SettingsFieldItem : ObservableObject
{
    /// <summary>创建字段。</summary>
    /// <param name="label">字段名。</param>
    /// <param name="value">当前值。</param>
    /// <param name="kind">呈现方式。</param>
    /// <param name="hint">字段下方的补充说明。</param>
    /// <param name="configPath">config.json 的点分路径（null = 只读展示，不进 config.set）。</param>
    /// <param name="isConfigList">值是否按行拆成字符串数组（denyPatterns 等）。</param>
    public SettingsFieldItem(
        string label,
        string value,
        SettingsFieldKind kind = SettingsFieldKind.Text,
        string? hint = null,
        string? configPath = null,
        bool isConfigList = false)
    {
        Label = label;
        Kind = kind;
        Hint = hint;
        ConfigPath = configPath;
        IsConfigList = isConfigList;
        OriginalValue = value;
        Value = value;
    }

    /// <summary>字段名。</summary>
    public string Label { get; }

    /// <summary>呈现方式。</summary>
    public SettingsFieldKind Kind { get; }

    /// <summary>补充说明。</summary>
    public string? Hint { get; }

    /// <summary>config.json 的点分路径（null = 只读）。</summary>
    public string? ConfigPath { get; }

    /// <summary>是否按行拆成字符串数组。</summary>
    public bool IsConfigList { get; }

    /// <summary>加载 / 上次保存后的值。</summary>
    public string OriginalValue { get; private set; }

    /// <summary>当前值（编辑态下由输入框双向绑定）。</summary>
    [ObservableProperty]
    public partial string Value { get; set; }

    /// <summary>这一页是否处于编辑态（只有可编辑字段会被置 true）。</summary>
    [ObservableProperty]
    public partial bool IsEditing { get; set; }

    /// <summary>是否单行文本 / 数字。</summary>
    public bool IsText => Kind is SettingsFieldKind.Text or SettingsFieldKind.Number;

    /// <summary>是否开关。</summary>
    public bool IsSwitch => Kind == SettingsFieldKind.Switch;

    /// <summary>是否多行文本。</summary>
    public bool IsMultiline => Kind == SettingsFieldKind.Multiline;

    /// <summary>是否只读说明。</summary>
    public bool IsReadOnly => Kind == SettingsFieldKind.ReadOnly;

    /// <summary>是否有 config.json 路径（决定能不能编辑并保存）。</summary>
    public bool CanEdit => ConfigPath is { Length: > 0 } && Kind != SettingsFieldKind.ReadOnly;

    /// <summary>是否改过。</summary>
    public bool IsDirty => !string.Equals(Value, OriginalValue, StringComparison.Ordinal);

    /// <summary>开关是否打开（双向绑定到 <see cref="Value"/>）。</summary>
    public bool IsOn
    {
        get => string.Equals(Value, "true", StringComparison.OrdinalIgnoreCase);
        set => Value = value ? "true" : "false";
    }

    /// <summary>是否有补充说明。</summary>
    public bool HasHint => !string.IsNullOrWhiteSpace(Hint);

    /// <summary>复制一个字段并用运行值替换（保留标签 / 类型 / 路径）。</summary>
    /// <param name="value">新值。</param>
    /// <param name="path">config.json 路径。</param>
    /// <param name="isList">是否字符串数组。</param>
    public SettingsFieldItem WithLive(string? value, string? path, bool isList)
        => new(Label, value ?? Value, Kind, Hint, path, isList);

    /// <summary>保存成功后把当前值设为新的基准。</summary>
    public void MarkSaved()
    {
        OriginalValue = Value;
        OnPropertyChanged(nameof(IsDirty));
    }

    /// <summary>取消编辑：值回到上次保存的基准，并退出编辑态。</summary>
    public void CancelEdit()
    {
        Value = OriginalValue;
        IsEditing = false;
    }

    /// <summary>进入编辑态（不可编辑字段保持只读）。</summary>
    public void BeginEdit() => IsEditing = CanEdit;

    partial void OnValueChanged(string value)
    {
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(IsOn));
    }
}
