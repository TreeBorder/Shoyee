using Shoyee.Contracts.Domain;
using Shoyee.Storage.Abstractions;

namespace Shoyee.Sessions.Lifecycle;

/// <summary>
/// 工作会话的创建与轮转（只碰会话表，不跑模型、不拼提示词）：
/// 没有活跃会话就建一条；跨天轮转时归档旧的、建新的（parent = 旧会话）并把 Lane 指针指过去。
///
/// **这不是常驻会话**：这里产出的是 <see cref="SessionRecord"/>（库里的元数据与指针），
/// 内存里的 <c>AgentSession</c> 由 Agent 运行时持有；resume 时先经这里找到记录，
/// 再用 <c>ShoyeeAgentSessionStore</c> 从 <c>session_state</c> 反序列化整包。
///
/// 返回被轮转的旧会话，调用方据此在首条消息前拼 <see cref="ContinuityNote"/>。
/// </summary>
public sealed class SessionFactory
{
    private readonly ISessionRepository _sessions;
    private readonly Func<string> _newId;

    /// <summary>创建工厂。</summary>
    /// <param name="sessions">会话仓库。</param>
    /// <param name="newId">会话 id 生成器（测试可注入）。</param>
    public SessionFactory(ISessionRepository sessions, Func<string>? newId = null)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        _sessions = sessions;
        _newId = newId ?? (static () => "ses_" + Guid.NewGuid().ToString("N"));
    }

    /// <summary>取该 Lane 的活跃会话；没有就建一条并设为当前。</summary>
    /// <param name="sessionKey">入站寻址键。</param>
    /// <param name="origin">来源渠道。</param>
    /// <param name="mode">模式（工作）。</param>
    /// <param name="laneId">所属 Lane。</param>
    /// <param name="now">创建时刻（默认现在）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<SessionRecord> GetOrCreateAsync(
        string sessionKey,
        LaneChannel origin,
        Mode mode,
        string laneId,
        DateTimeOffset? now = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(laneId);

        var active = await _sessions.FindActiveAsync(laneId, cancellationToken).ConfigureAwait(false);
        return active ?? await CreateAsync(
                sessionKey, origin, mode, laneId, now: now, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 入站消息到达时判定轮转：过期则归档旧会话、建新会话（parent = 旧会话）、更新 Lane 指针。
    /// </summary>
    /// <param name="sessionKey">入站寻址键。</param>
    /// <param name="origin">来源渠道。</param>
    /// <param name="mode">模式（工作）。</param>
    /// <param name="laneId">所属 Lane。</param>
    /// <param name="now">判定时刻（默认现在）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>被轮转的旧会话；不需要轮转时返回 null（并刷新活跃时间）。</returns>
    public async Task<SessionRecord?> RotateIfStaleAsync(
        string sessionKey,
        LaneChannel origin,
        Mode mode,
        string laneId,
        DateTimeOffset? now = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(laneId);

        var at = now ?? DateTimeOffset.UtcNow;
        var active = await _sessions.FindActiveAsync(laneId, cancellationToken).ConfigureAwait(false);
        if (active is null)
        {
            return null;
        }

        if (!SessionRotation.IsStale(active.LastActiveAt, at))
        {
            await _sessions.TouchAsync(active.Id, at, cancellationToken).ConfigureAwait(false);
            return null;
        }

        // 先归档旧会话，再建新会话：顺序反了会把刚建的新会话一起归档
        await _sessions.ArchiveAsync(active.Id, cancellationToken).ConfigureAwait(false);
        await CreateAsync(
                sessionKey, origin, mode, laneId,
                parentSessionId: active.Id, now: at, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return active with { Archived = true };
    }

    /// <summary>轮转后的续接提示：拼在新会话第一条用户消息前，指回已归档的旧会话。</summary>
    /// <param name="rotated">被轮转的旧会话。</param>
    public static string ContinuityNote(SessionRecord rotated)
    {
        ArgumentNullException.ThrowIfNull(rotated);
        return $"[系统提示：换天自动开了新会话（本条是新会话第一条），上一个会话已归档：{rotated.Id}"
            + $"「{rotated.Title}」（最后活动 {rotated.LastActiveAt.ToLocalTime():MM-dd HH:mm}）。"
            + "若用户提到之前（含昨天）聊过的事，先用 session_search 查回那个会话的内容再回答，别凭印象编。]\n\n";
    }

    /// <summary>
    /// 新建一条工作会话并（有 Lane 时）把该 Lane 的当前会话指针指过来。
    /// 供显式 <c>session.create</c> 与跨天轮转共用。
    /// </summary>
    /// <param name="sessionKey">入站寻址键；null = 由 Lane 与自动 id 生成（用户手动新建的会话不走入站匹配）。</param>
    /// <param name="origin">来源渠道。</param>
    /// <param name="mode">模式（工作）。</param>
    /// <param name="laneId">所属 Lane；null = 不属于任何 Lane。</param>
    /// <param name="title">标题；null = 默认「会话 时间」。</param>
    /// <param name="parentSessionId">父会话（轮转血缘）。</param>
    /// <param name="now">创建时刻（默认现在）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<SessionRecord> CreateAsync(
        string? sessionKey,
        LaneChannel origin,
        Mode mode,
        string? laneId,
        string? title = null,
        string? parentSessionId = null,
        DateTimeOffset? now = null,
        CancellationToken cancellationToken = default)
    {
        var at = now ?? DateTimeOffset.UtcNow;
        var id = _newId();
        var key = string.IsNullOrWhiteSpace(sessionKey) ? $"{laneId ?? "desktop:work"}:{id}" : sessionKey;
        var session = new SessionRecord(
            id,
            mode,
            origin,
            laneId,
            key,
            string.IsNullOrWhiteSpace(title) ? $"会话 {at.ToLocalTime():yyyy-MM-dd HH:mm}" : title.Trim(),
            parentSessionId,
            at,
            at,
            Archived: false);

        var created = await _sessions.CreateAsync(session, cancellationToken).ConfigureAwait(false);
        if (laneId is not null)
        {
            await _sessions.SetActiveForLaneAsync(laneId, created.Id, cancellationToken).ConfigureAwait(false);
        }

        return created;
    }
}
