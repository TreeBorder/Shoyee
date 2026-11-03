using CommunityToolkit.Mvvm.ComponentModel;

namespace Shoyee.Desktop.ViewModels.Items;

/// <summary>RP 头部渠道 / Lane 切换项。</summary>
public sealed partial class LaneItem : ObservableObject
{
    /// <summary>Lane id（展示用）。</summary>
    public required string Id { get; init; }

    /// <summary>渠道展示名。</summary>
    public required string Name { get; init; }

    /// <summary>状态说明。</summary>
    public required string Detail { get; init; }

    /// <summary>最近一条消息预览。</summary>
    public string Preview { get; init; } = "";

    /// <summary>最近一条消息时间。</summary>
    public string LastTime { get; init; } = "";

    /// <summary>消息条数。</summary>
    public int MessageCount { get; init; }

    /// <summary>渠道字形。</summary>
    public required string Glyph { get; init; }

    /// <summary>是否连接正常。</summary>
    public bool Connected { get; init; }

    /// <summary>是否当前查看的渠道（发送渠道固定桌面，见 <see cref="IsDesktop"/>）。</summary>
    [ObservableProperty]
    public partial bool IsCurrent { get; set; }

    /// <summary>是否桌面（桌面 Lane 恒在线）。</summary>
    public bool IsDesktop { get; init; }

    /// <summary>是否断开（给圆点配色用）。</summary>
    public bool IsDisconnected => !Connected;
}
