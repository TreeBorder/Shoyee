namespace Shoyee.Notes;

/// <summary>
/// 可选能力：笔记源落在本地文件系统上（Obsidian vault 等）。
/// 实现方要额外提供根目录，供两处使用：审批白名单 / RP 写围栏、以及「用文件工具做精细编辑」的路径提示。
/// 远端 provider（Notion 之类）不实现这个接口。
/// </summary>
public interface ILocalNotesFiles
{
    /// <summary>本地根目录（绝对路径）；未配置时为 null。</summary>
    string? Root { get; }

    /// <summary>新笔记默认落盘目录（绝对路径）；未配置时为 null。</summary>
    string? DefaultDirectory { get; }
}
