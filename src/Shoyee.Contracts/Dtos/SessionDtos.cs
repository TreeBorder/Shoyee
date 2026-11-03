using Shoyee.Contracts.Domain;

namespace Shoyee.Contracts.Dtos;

/// <summary>会话列表项。</summary>
/// <param name="Id">会话 id。</param>
/// <param name="Mode">模式。</param>
/// <param name="Origin">来源渠道。</param>
/// <param name="LaneId">来源 Lane（桌面 RP 亦有）。</param>
/// <param name="Title">标题。</param>
/// <param name="Archived">是否归档。</param>
/// <param name="CreatedAt">创建时间（UTC）。</param>
/// <param name="LastActiveAt">最后活跃时间（UTC）。</param>
/// <param name="MessageCount">消息条数。</param>
public sealed record SessionSummary(
    string Id,
    Mode Mode,
    LaneChannel Origin,
    string? LaneId,
    string? Title,
    bool Archived,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastActiveAt,
    int MessageCount);

/// <summary>会话详情：列表字段 + 冻结提示词/工具集的摘要信息。</summary>
/// <param name="Id">会话 id。</param>
/// <param name="Mode">模式。</param>
/// <param name="Origin">来源渠道。</param>
/// <param name="LaneId">来源 Lane。</param>
/// <param name="Title">标题。</param>
/// <param name="Archived">是否归档。</param>
/// <param name="CreatedAt">创建时间（UTC）。</param>
/// <param name="LastActiveAt">最后活跃时间（UTC）。</param>
/// <param name="MessageCount">消息条数。</param>
/// <param name="SessionKey">入站寻址键。</param>
/// <param name="ParentSessionId">父会话（轮转血缘）。</param>
/// <param name="IsActiveForLane">是否是本 Lane 的当前会话。</param>
public sealed record SessionDetail(
    string Id,
    Mode Mode,
    LaneChannel Origin,
    string? LaneId,
    string? Title,
    bool Archived,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastActiveAt,
    int MessageCount,
    string SessionKey,
    string? ParentSessionId,
    bool IsActiveForLane);

/// <summary><c>session.snapshot</c> 的返回：工作模式开场那条 system 的正文（从会话状态取）。</summary>
/// <param name="Text">开场提示词全文；RP 会话返回空（每轮组装，不落库）。</param>
public sealed record SessionSnapshot(string Text);

/// <summary><c>session.list</c> 的请求。</summary>
/// <param name="Mode">模式过滤。</param>
/// <param name="LaneId">Lane 过滤。</param>
/// <param name="IncludeArchived">是否包含已归档。</param>
/// <param name="Limit">本页条数（1–200）。</param>
/// <param name="Cursor">上一页最后一条的会话 id；null = 第一页。</param>
public sealed record SessionListRequest(
    Mode? Mode = null,
    string? LaneId = null,
    bool IncludeArchived = false,
    int Limit = 50,
    string? Cursor = null);

/// <summary><c>session.create</c> 的请求。</summary>
/// <param name="Mode">模式；只有 <see cref="Mode.Work"/> 能建会话（RP 没有会话）。</param>
/// <param name="LaneId">所属 Lane；null = 不属于任何 Lane 的桌面工作会话。</param>
/// <param name="Title">标题；null = 默认「会话 时间」。</param>
public sealed record SessionCreateRequest(Mode Mode, string? LaneId = null, string? Title = null);

/// <summary>只带会话 id 的请求（get / open / archive）。</summary>
/// <param name="SessionId">会话 id。</param>
public sealed record SessionIdRequest(string SessionId);

/// <summary><c>session.rename</c> 的请求。</summary>
/// <param name="SessionId">会话 id。</param>
/// <param name="Title">新标题。</param>
public sealed record SessionRenameRequest(string SessionId, string Title);

/// <summary><c>session.setActiveForLane</c> 的请求。</summary>
/// <param name="SessionId">会话 id。</param>
/// <param name="LaneId">Lane id。</param>
public sealed record SessionActiveForLaneRequest(string SessionId, string LaneId);

/// <summary><c>session.messages</c> 的请求。</summary>
/// <param name="SessionId">会话 id。</param>
/// <param name="BeforeId">取该消息 id 之前的一页。</param>
/// <param name="AfterId">取该消息 id 之后的一页（与 beforeId 互斥）。</param>
/// <param name="Limit">本页条数（1–200）。</param>
public sealed record SessionMessagesRequest(
    string SessionId,
    long? BeforeId = null,
    long? AfterId = null,
    int Limit = 50);

/// <summary><c>lane.messages</c> 的请求。</summary>
/// <param name="LaneId">Lane id。</param>
/// <param name="BeforeId">取该消息 id 之前的一页。</param>
/// <param name="AfterId">取该消息 id 之后的一页（与 beforeId 互斥）。</param>
/// <param name="Limit">本页条数（1–200）。</param>
public sealed record LaneMessagesRequest(
    string LaneId,
    long? BeforeId = null,
    long? AfterId = null,
    int Limit = 50);

/// <summary><c>session.search</c> 的请求。</summary>
/// <param name="Query">关键词。</param>
/// <param name="Mode">限定模式；null 为不限。</param>
/// <param name="LaneId">限定 Lane；null 为不限。</param>
/// <param name="Limit">最多返回条数。</param>
public sealed record SessionSearchRequest(string Query, Mode? Mode = null, string? LaneId = null, int Limit = 20);

/// <summary><c>session.search</c> 的返回。</summary>
/// <param name="Items">命中。</param>
public sealed record SessionSearchResult(IReadOnlyList<SearchHit> Items);


/// <summary>分页结果通用外壳。</summary>
/// <typeparam name="T">元素类型。</typeparam>
/// <param name="Items">本页元素。</param>
/// <param name="NextCursor">下一页游标；null 表示最后一页。</param>
public sealed record PageResult<T>(IReadOnlyList<T> Items, string? NextCursor = null);

/// <summary>分页消息结果。</summary>
/// <param name="Items">消息（按 seq 升序）。</param>
/// <param name="HasMore">是否还有更多。</param>
/// <param name="Context">该会话最后一次主对话调用的上下文占用；没有 usage 记录时为 null。</param>
public sealed record SessionMessagesResult(
    IReadOnlyList<MessageView> Items,
    bool HasMore,
    ContextUsage? Context = null);

/// <summary>
/// <c>session.renamed</c> 载荷：会话标题变了（当前只有自动起名会推）。
/// 桌面据此原地改列表项与标题，不必重拉整个会话列表。
/// </summary>
/// <param name="SessionId">会话 id。</param>
/// <param name="Title">新标题。</param>
/// <param name="LaneId">所属 Lane（事件可见性过滤）。</param>
public sealed record SessionRenamedEvent(string SessionId, string Title, string? LaneId = null);

/// <summary><c>session.export</c> 的请求。</summary>
/// <param name="SessionId">要导出的工作会话。</param>
/// <param name="Format">导出格式：<c>md</c>（默认）或 <c>json</c>。</param>
public sealed record SessionExportRequest(string SessionId, string Format = "md");

/// <summary><c>session.export</c> 的返回。</summary>
/// <param name="Path">导出文件的绝对路径（本机可直读）。</param>
public sealed record SessionExportResult(string Path);
