namespace Shoyee.Desktop.ViewModels.Settings;

/// <summary>设置页占位表单里的一张卡片（一组字段）。</summary>
/// <param name="Title">分组标题。</param>
/// <param name="Description">分组说明。</param>
/// <param name="Fields">字段。</param>
public sealed record SettingsGroupItem(
    string Title,
    string Description,
    IReadOnlyList<SettingsFieldItem> Fields);
