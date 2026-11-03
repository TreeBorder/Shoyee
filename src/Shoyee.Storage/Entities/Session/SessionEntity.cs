using Shoyee.Contracts.Domain;

namespace Shoyee.Storage.Entities.Session;

/// <summary>工作会话。</summary>
public sealed class SessionEntity
{
    /// <summary>会话 id。</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>模式。</summary>
    public Mode Mode { get; set; }

    /// <summary>来源渠道。</summary>
    public LaneChannel Origin { get; set; }

    /// <summary>来源 Lane。</summary>
    public string? LaneId { get; set; }

    /// <summary>入站寻址键。</summary>
    public string SessionKey { get; set; } = string.Empty;

    /// <summary>标题。</summary>
    public string? Title { get; set; }

    /// <summary>父会话（轮转血缘）。</summary>
    public string? ParentSessionId { get; set; }

    /// <summary>创建时间（UTC）。</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>最后活跃时间（UTC）。</summary>
    public DateTimeOffset LastActiveAt { get; set; }

    /// <summary>是否归档。</summary>
    public bool Archived { get; set; }
}
