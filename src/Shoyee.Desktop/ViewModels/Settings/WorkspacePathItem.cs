namespace Shoyee.Desktop.ViewModels.Settings;

/// <summary>设置页要展示的一个本地路径。</summary>
public sealed class WorkspacePathItem
{
    /// <summary>名称（配置 / 人设 / 日志等）。</summary>
    public required string Label { get; init; }

    /// <summary>绝对路径。</summary>
    public required string Path { get; init; }
}
