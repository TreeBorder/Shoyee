namespace Shoyee.Storage.Entities.Ops;

/// <summary>通用状态（照 Harem <c>StateItem</c>）：键值 + 说明 + 是否可由她修改。</summary>
public sealed class StateEntity
{
    /// <summary>稳定键（主键，如 <c>longing</c>）。</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>展示名。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>当前值（统一字符串；数值由消费者解析）。</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>给模型的说明与取值约束。</summary>
    public string? Description { get; set; }

    /// <summary>她是否可以自己改。</summary>
    public bool AgentEditable { get; set; } = true;

    /// <summary>更新时间（UTC）。</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
