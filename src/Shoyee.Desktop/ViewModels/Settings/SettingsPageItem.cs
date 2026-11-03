namespace Shoyee.Desktop.ViewModels.Settings;

/// <summary>
/// 设置页的一个子页：标题、数据来源与待接入项。
/// 有真实界面时 <see cref="Status"/> 为 <see cref="SettingsPageStatus.Live"/>，其余走占位视图。
/// </summary>
/// <param name="Key">子页键，稳定不变，供命令与测试引用。</param>
/// <param name="Title">子页名称（同时是子页签文案）。</param>
/// <param name="Summary">一句话说明这一页负责什么。</param>
/// <param name="DataSource">数据从哪里来（配置项、RPC 方法或工作区文件）。</param>
/// <param name="Status">完成度。</param>
/// <param name="Planned">接入前还差什么；每项一句话。</param>
/// <param name="Groups">结构化占位表单；有真实视图的页可以为空。</param>
public sealed record SettingsPageItem(
    string Key,
    string Title,
    string Summary,
    string DataSource,
    SettingsPageStatus Status,
    IReadOnlyList<string> Planned,
    IReadOnlyList<SettingsGroupItem>? Groups = null);
