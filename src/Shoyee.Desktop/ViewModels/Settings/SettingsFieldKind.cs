namespace Shoyee.Desktop.ViewModels.Settings;

/// <summary>设置页占位表单里一个字段的呈现方式。</summary>
public enum SettingsFieldKind
{
    /// <summary>单行文本。</summary>
    Text,

    /// <summary>数字（仍按文本显示，避免占位里做校验）。</summary>
    Number,

    /// <summary>开关。</summary>
    Switch,

    /// <summary>多行文本（提示词、列表、JSON 等）。</summary>
    Multiline,

    /// <summary>只读说明。</summary>
    ReadOnly,
}
