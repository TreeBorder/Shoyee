using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shoyee.Desktop.Theming;
using Shoyee.Desktop.ViewModels.Items;

namespace Shoyee.Desktop.ViewModels;

/// <summary>
/// 会话页共用的偏好：内容宽度、工具 / 思考显示、发送快捷键。
///
/// 这些是「看对话」的本机偏好，跟着 desktop.json 走；真正的消息内容由各页自己的
/// <see cref="ApplyMessageFilter"/> 过滤成可见列表。
/// </summary>
public abstract partial class ConversationLayoutViewModel : ViewModelBase
{
    /// <summary>是否铺满宽度（默认 true）。</summary>
    [ObservableProperty]
    public partial bool IsFullWidth { get; set; } = true;

    /// <summary>是否显示工具调用行（默认显示；每行仍然默认折叠）。</summary>
    [ObservableProperty]
    public partial bool ShowToolCalls { get; set; } = true;

    /// <summary>是否显示思考过程（默认不显示）。</summary>
    [ObservableProperty]
    public partial bool ShowReasoning { get; set; }

    /// <summary>是否显示系统注入消息（心跳 / 定时 / 后台回执；默认不显示，免得混进对话）。</summary>
    [ObservableProperty]
    public partial bool ShowSystemInputs { get; set; }

    /// <summary>发送快捷键是否为 Enter（false = Enter 换行、⌘/Ctrl+Enter 发送）。</summary>
    [ObservableProperty]
    public partial bool SendOnEnter { get; set; }

    /// <summary>气泡是否平铺（false = 当前窄气泡，正文最宽 620）。</summary>
    [ObservableProperty]
    public partial bool IsWideBubbles { get; set; }

    /// <summary>聊天列表与输入框的最大宽度：铺满时给一个足够大的值，居中时 780。</summary>
    public double ContentMaxWidth => IsFullWidth ? 2400 : 780;

    /// <summary>是否居中窄栏（给分段按钮的 active 状态用）。</summary>
    public bool IsCenteredWidth => !IsFullWidth;

    /// <summary>输入框下方的快捷键提示。</summary>
    public string ShortcutHint => SendOnEnter
        ? "Enter 发送 · Shift+Enter 换行"
        : "Enter 换行 · ⌘/Ctrl+Enter 发送";

    /// <summary>工具显示开关的提示。</summary>
    public string ToolCallsTip => ShowToolCalls
        ? "工具调用：显示中（点击隐藏；每条默认折叠）"
        : "工具调用：已隐藏（点击显示）";

    /// <summary>思考显示开关的提示。</summary>
    public string ReasoningTip => ShowReasoning
        ? "思考过程：显示中（点击隐藏）"
        : "思考过程：已隐藏（点击显示）";

    /// <summary>系统消息显示开关的提示。</summary>
    public string SystemInputsTip => ShowSystemInputs
        ? "系统消息：显示中（心跳 / 定时 / 后台回执；点击隐藏）"
        : "系统消息：已隐藏（点击按用户消息查看）";

    /// <summary>发送快捷键开关的提示。</summary>
    public string SendShortcutTip => SendOnEnter
        ? "当前 Enter 发送、Shift+Enter 换行；点击改成 Enter 换行"
        : "当前 Enter 换行、⌘/Ctrl+Enter 发送；点击改成 Enter 发送";

    /// <summary>气泡宽度开关的提示。</summary>
    public string BubbleWidthTip => IsWideBubbles
        ? "气泡：平铺（对端头像列外再留一个头像宽度）；点击回到 620 窄气泡"
        : "气泡：当前款（正文最宽 620，工具与思考跟随）；点击平铺";

    /// <summary>气泡宽度按钮的图标。</summary>
    public Geometry BubbleWidthIcon => IsWideBubbles
        ? IconGeometries.WidthWide
        : IconGeometries.WidthNormal;

    /// <summary>按当前开关重建可见消息列表。</summary>
    protected abstract void ApplyMessageFilter();

    /// <summary>
    /// 把 <paramref name="visible"/> 增量同步到 <paramref name="source"/> 里通过筛选的项：
    /// 只增删 / 移动差异项，不整表 Clear——整表重画会让所有气泡重建、滚动位置和入场动画一起跳。
    /// </summary>
    /// <param name="visible">界面绑定的可见集合。</param>
    /// <param name="source">完整消息集合（顺序即显示顺序）。</param>
    /// <param name="passes">是否可见。</param>
    protected static void SyncVisible(
        System.Collections.ObjectModel.ObservableCollection<ChatMessageItem> visible,
        IReadOnlyList<ChatMessageItem> source,
        Func<ChatMessageItem, bool> passes)
    {
        var keep = new HashSet<ChatMessageItem>(source.Where(passes));
        for (var i = visible.Count - 1; i >= 0; i--)
        {
            if (!keep.Contains(visible[i]))
            {
                visible.RemoveAt(i);
            }
        }

        var target = 0;
        foreach (var item in source)
        {
            if (!passes(item))
            {
                continue;
            }

            if (target < visible.Count && ReferenceEquals(visible[target], item))
            {
                target++;
                continue;
            }

            var existing = -1;
            for (var i = target; i < visible.Count; i++)
            {
                if (ReferenceEquals(visible[i], item))
                {
                    existing = i;
                    break;
                }
            }

            if (existing >= 0)
            {
                visible.Move(existing, target);
            }
            else
            {
                visible.Insert(target, item);
            }

            target++;
        }
    }

    /// <summary>切到铺满。</summary>
    [RelayCommand]
    private void SetFullWidth() => IsFullWidth = true;

    /// <summary>切到居中窄栏。</summary>
    [RelayCommand]
    private void SetCenteredWidth() => IsFullWidth = false;

    /// <summary>显示 / 隐藏工具调用行。</summary>
    [RelayCommand]
    private void ToggleToolCalls() => ShowToolCalls = !ShowToolCalls;

    /// <summary>显示 / 隐藏思考过程。</summary>
    [RelayCommand]
    private void ToggleReasoning() => ShowReasoning = !ShowReasoning;

    /// <summary>显示 / 隐藏系统注入消息。</summary>
    [RelayCommand]
    private void ToggleSystemInputs() => ShowSystemInputs = !ShowSystemInputs;

    /// <summary>在「Enter 发送」和「Enter 换行」之间切换。</summary>
    [RelayCommand]
    private void ToggleSendShortcut() => SendOnEnter = !SendOnEnter;

    /// <summary>在当前窄气泡和平铺气泡之间切换。</summary>
    [RelayCommand]
    private void ToggleBubbleWidth() => IsWideBubbles = !IsWideBubbles;

    partial void OnIsFullWidthChanged(bool value)
    {
        OnPropertyChanged(nameof(ContentMaxWidth));
        OnPropertyChanged(nameof(IsCenteredWidth));
    }

    partial void OnShowToolCallsChanged(bool value)
    {
        OnPropertyChanged(nameof(ToolCallsTip));
        ApplyMessageFilter();
    }

    partial void OnShowReasoningChanged(bool value)
    {
        OnPropertyChanged(nameof(ReasoningTip));
        ApplyMessageFilter();
    }

    partial void OnShowSystemInputsChanged(bool value)
    {
        OnPropertyChanged(nameof(SystemInputsTip));
        ApplyMessageFilter();
    }

    partial void OnSendOnEnterChanged(bool value)
    {
        OnPropertyChanged(nameof(ShortcutHint));
        OnPropertyChanged(nameof(SendShortcutTip));
        OnPropertyChanged(nameof(ComposerWatermark));
    }

    partial void OnIsWideBubblesChanged(bool value)
    {
        OnPropertyChanged(nameof(BubbleWidthTip));
        OnPropertyChanged(nameof(BubbleWidthIcon));
        ApplyMessageFilter();
    }

    /// <summary>输入框占位文案；各页在末尾拼上当前快捷键。</summary>
    public abstract string ComposerWatermark { get; }
}
