using CommunityToolkit.Mvvm.ComponentModel;

namespace Shoyee.Desktop.ViewModels.Items;

/// <summary>左侧会话列表的项（示例数据与 RPC 数据共用）。</summary>
public sealed class SessionItem : ObservableObject
{
    private string _title = "";

    /// <summary>会话 id；空 = 来自导入快照，不是 daemon 里的会话。</summary>
    public string Id { get; init; } = "";

    /// <summary>是否 daemon 里的真实会话（决定能否发消息 / 新建）。</summary>
    public bool IsLive => !string.IsNullOrWhiteSpace(Id);

    /// <summary>标题（自动起名 / 改名后原地更新）。</summary>
    public required string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    /// <summary>最后一条预览。</summary>
    public required string Preview { get; init; }

    /// <summary>相对时间。</summary>
    public required string Time { get; init; }

    /// <summary>来源渠道标签。</summary>
    public required string Source { get; init; }

    /// <summary>来源是否外部渠道（徽标配色不同）。</summary>
    public bool IsExternal { get; init; }

    /// <summary>来源是否桌面（给徽标配色用）。</summary>
    public bool IsDesktopSource => !IsExternal;

    /// <summary>是否显示渠道图标（导入的未知来源不显示）。</summary>
    public bool HasChannelTag => !string.IsNullOrWhiteSpace(Source);

    /// <summary>是否当前打开。</summary>
    public bool IsActive { get; init; }

    /// <summary>是否置顶（置顶项排在最前）。</summary>
    public bool IsPinned { get; init; }

    /// <summary>置顶标记。</summary>
    public bool HasPin => IsPinned;

    /// <summary>鼠标悬停时的预览：最后一条消息 + 时间/来源。</summary>
    public string HoverPreview => $"{Preview}\n{Time} · {Source}";

    /// <summary>消息条数。</summary>
    public int MessageCount { get; init; }

    /// <summary>创建时间（筛选依据）。</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>创建时间的展示文本（右侧状态栏）。</summary>
    public string CreatedText { get; init; } = "";

    /// <summary>Lane id（右侧状态栏）。</summary>
    public string LaneId { get; init; } = "";

    /// <summary>上轮 LLM 调用的输入 token（0 = 没有 usage）。</summary>
    public long LastInputTokens { get; init; }

    /// <summary>上轮输入中命中缓存的 token。</summary>
    public long LastCachedInputTokens { get; init; }

    /// <summary>该会话的消息时间线（选中后填进聊天区）。</summary>
    public IReadOnlyList<ChatMessageItem> Messages { get; init; } = [];

    /// <summary>是否归档。</summary>
    public bool IsArchived { get; init; }

    /// <summary>未读条数（0 不显示）。</summary>
    public int Unread { get; init; }

    /// <summary>是否有未读。</summary>
    public bool HasUnread => Unread > 0;

    /// <summary>未读文本。</summary>
    public string UnreadText => Unread > 99 ? "99+" : Unread.ToString();
}
