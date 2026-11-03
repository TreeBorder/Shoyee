namespace Shoyee.Desktop.ViewModels.Settings;

/// <summary>设置子页的完成度。</summary>
public enum SettingsPageStatus
{
    /// <summary>已有真实界面（数据可能仍是占位内容）。</summary>
    Live,

    /// <summary>结构化占位：标题、数据来源与待接入项已定，内容等接后端。</summary>
    Planned,
}
