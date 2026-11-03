using Shoyee.Contracts.Domain;

namespace Shoyee.Storage.Abstractions;

/// <summary>一个工作会话。</summary>
/// <param name="Id">会话 id。</param>
/// <param name="Mode">模式。</param>
/// <param name="Origin">来源渠道。</param>
/// <param name="LaneId">来源 Lane。</param>
/// <param name="SessionKey">入站寻址键。</param>
/// <param name="Title">标题。</param>
/// <param name="ParentSessionId">轮转血缘。</param>
/// <param name="CreatedAt">创建时间。</param>
/// <param name="LastActiveAt">最后活跃时间。</param>
/// <param name="Archived">是否归档。</param>
public sealed record SessionRecord(
    string Id,
    Mode Mode,
    LaneChannel Origin,
    string? LaneId,
    string SessionKey,
    string? Title,
    string? ParentSessionId,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastActiveAt,
    bool Archived);
