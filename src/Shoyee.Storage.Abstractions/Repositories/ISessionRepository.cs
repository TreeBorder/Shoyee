using Shoyee.Contracts.Domain;

namespace Shoyee.Storage.Abstractions;

/// <summary>工作会话仓库（RP 没有 session，不写这张表）。</summary>
public interface ISessionRepository
{
    /// <summary>按 id 取。</summary>
    Task<SessionRecord?> GetAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 该 Lane 的当前会话：指针有效且未归档就用它，否则回落到最近未归档会话。
    /// 不预建会话（避免 /new 多出空父级）。
    /// </summary>
    Task<SessionRecord?> FindActiveAsync(string laneId, CancellationToken cancellationToken = default);

    /// <summary>列表（按最后活跃倒序）。</summary>
    Task<IReadOnlyList<SessionRecord>> ListAsync(
        Mode? mode = null,
        string? laneId = null,
        bool includeArchived = false,
        int limit = 50,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 游标分页（按「最后活跃时间倒序 + id 倒序」keyset）：
    /// 只取严格排在 (<paramref name="beforeLastActiveAt"/>, <paramref name="beforeId"/>) 之后的记录。
    /// 两者都为 null = 第一页。
    /// </summary>
    Task<IReadOnlyList<SessionRecord>> ListPageAsync(
        Mode? mode,
        string? laneId,
        bool includeArchived,
        DateTimeOffset? beforeLastActiveAt,
        string? beforeId,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>新建。</summary>
    Task<SessionRecord> CreateAsync(SessionRecord session, CancellationToken cancellationToken = default);

    /// <summary>改标题。</summary>
    Task RenameAsync(string sessionId, string title, CancellationToken cancellationToken = default);

    /// <summary>归档（不删消息）。</summary>
    Task ArchiveAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>刷新最后活跃时间。</summary>
    Task TouchAsync(string sessionId, DateTimeOffset at, CancellationToken cancellationToken = default);

    /// <summary>把会话设为该 Lane 的当前会话。</summary>
    Task SetActiveForLaneAsync(string laneId, string sessionId, CancellationToken cancellationToken = default);
}
