namespace Shoyee.Desktop.ViewModels;

/// <summary>左侧导航当前页。</summary>
public enum AppSection
{
    /// <summary>当前对话（工作模式聊天）。</summary>
    Chat,

    /// <summary>定时任务。</summary>
    Cron,

    /// <summary>任务看板（后续接 Squad 等）。</summary>
    Board,

    /// <summary>收藏对话。</summary>
    Favorites,

    /// <summary>设置页（外观与桌面偏好）。</summary>
    Settings,
}
