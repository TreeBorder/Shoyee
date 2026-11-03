namespace Shoyee.Desktop.ViewModels.Items;

/// <summary>对话列表的创建时间筛选。</summary>
public enum SessionTimeFilter
{
    /// <summary>全部时间。</summary>
    All,

    /// <summary>今天创建。</summary>
    Today,

    /// <summary>最近 7 天创建。</summary>
    Last7Days,

    /// <summary>最近 30 天创建。</summary>
    Last30Days,
}
