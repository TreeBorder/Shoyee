using CommunityToolkit.Mvvm.ComponentModel;

namespace Shoyee.Desktop.ViewModels.Items;

/// <summary>心跳惊喜问候的一组提示词（Text / Story / Voice / Image）。</summary>
/// <param name="Kind">组名。</param>
/// <param name="Description">这一组怎么用。</param>
/// <param name="Lines">l1–l5 各一条。</param>
public sealed record HeartbeatPromptGroup(
    string Kind,
    string Description,
    IReadOnlyList<HeartbeatPromptLine> Lines);

/// <summary>心跳问候的一条提示词。</summary>
/// <param name="Level">档位（l1–l5）。</param>
/// <param name="Text">提示词正文；可以直接改。</param>
public sealed partial class HeartbeatPromptLine(string level, string text) : ObservableObject
{
    /// <summary>档位（l1–l5）。</summary>
    public string Level { get; } = level;

    /// <summary>提示词正文（可编辑，保存时写回 prompts.json）。</summary>
    [ObservableProperty]
    public partial string Text { get; set; } = text;
}

/// <summary>状态表的一行。</summary>
/// <param name="Name">状态项（scene / outfit 等）。</param>
/// <param name="Current">当前值。</param>
/// <param name="Default">默认值。</param>
/// <param name="Description">说明。</param>
public sealed record StateRowItem(
    string Name,
    string Current,
    string Default,
    string Description);

/// <summary>资料索引（INDEX）的一条。</summary>
/// <param name="Name">名称，唯一，工具按它定位。</param>
/// <param name="Comment">短注释，会注入到会话开局。</param>
/// <param name="Address">可直接读的地址（vault 路径 / 绝对路径 / URL）。</param>
/// <param name="IsEnabled">是否启用；停用后保留在表里但智能体取不到。</param>
public sealed record IndexRowItem(
    string Name,
    string Comment,
    string Address,
    bool IsEnabled = true);

/// <summary>子智能体的一行。</summary>
/// <param name="Name">英文标识（tools.subagents.agents[].name）。</param>
/// <param name="Title">中文名 + 岗位。</param>
/// <param name="Description">派什么活给它。</param>
/// <param name="Instructions">子会话的顶层系统提示词。</param>
/// <param name="Tools">工具说明。</param>
/// <param name="AllowShell">是否放行终端。</param>
/// <param name="IsEnabled">是否启用。</param>
public sealed record SubagentRowItem(
    string Name,
    string Title,
    string Description,
    string Instructions,
    string Tools,
    bool AllowShell,
    bool IsEnabled = true);

/// <summary>一条预置指令。</summary>
/// <param name="Command">指令写法（含参数）。</param>
/// <param name="Description">做什么。</param>
/// <param name="Note">补充说明。</param>
public sealed record PresetCommandItem(
    string Command,
    string Description,
    string Note);
