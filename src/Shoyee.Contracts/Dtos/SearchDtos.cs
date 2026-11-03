using Shoyee.Contracts.Domain;

namespace Shoyee.Contracts.Dtos;

/// <summary>会话检索命中。</summary>
/// <param name="SessionId">所属工作会话；RP 命中为 null。</param>
/// <param name="SessionTitle">会话标题（无标题时为空）。</param>
/// <param name="LaneId">所属 Lane（两种模式都有）。</param>
/// <param name="MessageId">消息 id。</param>
/// <param name="RoundNo">所属轮次；工作模式为 null。</param>
/// <param name="Snippet">围绕命中位置开窗的片段（不是整条消息）。</param>
/// <param name="Role">消息角色。</param>
/// <param name="Mode">会话模式。</param>
/// <param name="Origin">会话来源渠道。</param>
/// <param name="CreatedAt">消息时间（UTC）。</param>
public sealed record SearchHit(
    string? SessionId,
    string? SessionTitle,
    string LaneId,
    long MessageId,
    int? RoundNo,
    string Snippet,
    MessageRole Role,
    Mode Mode,
    LaneChannel Origin,
    DateTimeOffset CreatedAt);

/// <summary>检索过滤条件。</summary>
/// <param name="Mode">限定模式；null 为不限。</param>
/// <param name="LaneId">限定 Lane；null 为不限。</param>
/// <param name="Limit">最多返回条数。</param>
public sealed record SessionSearchFilter(Mode? Mode = null, string? LaneId = null, int Limit = 20);
