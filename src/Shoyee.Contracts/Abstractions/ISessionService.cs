using Shoyee.Contracts.Dtos;

namespace Shoyee.Contracts.Abstractions;

/// <summary>
/// 工作会话领域服务：Gateway / Handlers / 桌面共用的会话入口。
/// 只做校验与编排，不碰传输；错误一律抛 <see cref="Rpc.RpcException"/>。
/// </summary>
public interface ISessionService
{
    /// <summary>会话列表（按最后活跃倒序，带消息条数真值）。</summary>
    Task<PageResult<SessionSummary>> ListAsync(SessionListRequest request, CancellationToken cancellationToken = default);

    /// <summary>会话详情；不存在抛 <c>not_found</c>。已归档仍可查看。</summary>
    Task<SessionDetail> GetAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 打开会话（resume）：把框架 session 装进常驻缓存，不改 IM 指针。
    /// 不存在 / 已归档 / 状态缺失分别报错。
    /// </summary>
    Task<SessionDetail> OpenAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>新建工作会话：拼装快照提示词、建框架 session、落状态并入缓存。</summary>
    Task<SessionDetail> CreateAsync(SessionCreateRequest request, CancellationToken cancellationToken = default);

    /// <summary>改名；会话不存在抛 <c>not_found</c>。</summary>
    Task RenameAsync(string sessionId, string title, CancellationToken cancellationToken = default);

    /// <summary>归档（不是删除，消息仍在）；同时踢出常驻缓存。</summary>
    Task ArchiveAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>把会话显式设为该 Lane 的当前会话（IM 指针）。</summary>
    Task SetActiveForLaneAsync(string sessionId, string laneId, CancellationToken cancellationToken = default);

    /// <summary>工作会话消息分页（按消息 id 游标）；已归档仍可读。</summary>
    Task<SessionMessagesResult> GetMessagesAsync(
        string sessionId,
        long? beforeId = null,
        long? afterId = null,
        int limit = 50,
        CancellationToken cancellationToken = default);

    /// <summary>RP Lane 历史分页（只读视图，不参与每轮上下文组装）。</summary>
    Task<SessionMessagesResult> GetLaneMessagesAsync(
        string laneId,
        long? beforeId = null,
        long? afterId = null,
        int limit = 50,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 冻结提示词正文：从会话状态里取开场那条 system。
    /// 会话不存在 → <c>not_found</c>；状态缺失 → <c>internal</c>（<c>data.reason = state_missing</c>）。
    /// </summary>
    Task<SessionSnapshot> GetSnapshotAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>导出会话到 <c>{workspace}/exports/</c>；格式 <c>md</c> / <c>json</c>。</summary>
    Task<SessionExportResult> ExportAsync(
        SessionExportRequest request,
        CancellationToken cancellationToken = default);
}
