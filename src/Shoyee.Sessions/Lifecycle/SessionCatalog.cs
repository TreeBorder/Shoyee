using System.Text;
using System.Text.Json;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Rpc;
using Shoyee.Storage.Abstractions;

namespace Shoyee.Sessions.Lifecycle;

/// <summary>
/// 会话目录：列表 / 详情 / 改名 / 归档 / 消息分页。只碰仓库，不碰 Agent、不拼提示词。
///
/// 消息条数与分页都读 <c>messages</c> 投影（转录投影的派生数据），不读 <c>session_state</c>——
/// 状态包是框架的，解析它属于 Agent 运行时的事。
/// </summary>
public sealed class SessionCatalog
{
    /// <summary>单页最大条数。</summary>
    public const int MaxPageSize = 200;

    private readonly ISessionRepository _sessions;
    private readonly IMessageRepository _messages;
    private readonly ILaneRepository _lanes;
    private readonly string? _workspaceRoot;

    /// <summary>创建目录。</summary>
    /// <param name="sessions">会话仓库。</param>
    /// <param name="messages">消息投影仓库。</param>
    /// <param name="lanes">Lane 仓库。</param>
    /// <param name="workspaceRoot">工作区根（导出落盘用；null = 不支持导出）。</param>
    public SessionCatalog(
        ISessionRepository sessions,
        IMessageRepository messages,
        ILaneRepository lanes,
        string? workspaceRoot = null)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(lanes);
        _sessions = sessions;
        _messages = messages;
        _lanes = lanes;
        _workspaceRoot = workspaceRoot;
    }

    /// <summary>会话列表（按最后活跃倒序，带消息条数真值）。</summary>
    /// <param name="request">过滤与分页参数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<PageResult<SessionSummary>> ListAsync(
        SessionListRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var limit = ClampLimit(request.Limit);

        SessionRecord? cursor = null;
        if (!string.IsNullOrWhiteSpace(request.Cursor))
        {
            cursor = await _sessions.GetAsync(request.Cursor, cancellationToken).ConfigureAwait(false)
                     ?? throw new RpcException(
                         RpcErrors.InvalidParams, "cursor 指向的会话不存在", new { cursor = request.Cursor });
        }

        // 多取一条判断还有没有下一页
        var rows = await _sessions.ListPageAsync(
                request.Mode,
                request.LaneId,
                request.IncludeArchived,
                cursor?.LastActiveAt,
                cursor?.Id,
                limit + 1,
                cancellationToken)
            .ConfigureAwait(false);

        var hasMore = rows.Count > limit;
        var page = hasMore ? rows.Take(limit).ToList() : rows;
        var items = new List<SessionSummary>(page.Count);
        foreach (var row in page)
        {
            items.Add(await ToSummaryAsync(row, cancellationToken).ConfigureAwait(false));
        }

        return new PageResult<SessionSummary>(items, hasMore ? items[^1].Id : null);
    }

    /// <summary>会话详情；不存在抛 <c>not_found</c>。</summary>
    /// <param name="sessionId">会话 id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<SessionDetail> GetAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var session = await RequireAsync(sessionId, cancellationToken).ConfigureAwait(false);
        return await ToDetailAsync(session, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>改名；标题不能为空。</summary>
    /// <param name="sessionId">会话 id。</param>
    /// <param name="title">新标题。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task RenameAsync(string sessionId, string title, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new RpcException(RpcErrors.InvalidParams, "title 不能为空");
        }

        await RequireAsync(sessionId, cancellationToken).ConfigureAwait(false);
        await _sessions.RenameAsync(sessionId, title.Trim(), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>归档（幂等：已归档再归档不报错）。</summary>
    /// <param name="sessionId">会话 id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task ArchiveAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        await RequireAsync(sessionId, cancellationToken).ConfigureAwait(false);
        await _sessions.ArchiveAsync(sessionId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>把会话设为该 Lane 的当前会话。</summary>
    /// <param name="sessionId">会话 id。</param>
    /// <param name="laneId">Lane id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task SetActiveForLaneAsync(
        string sessionId,
        string laneId,
        CancellationToken cancellationToken = default)
    {
        var session = await RequireAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (session.Archived)
        {
            throw new RpcException(
                RpcErrors.SessionArchived, "会话已归档，不能设为当前会话", new { sessionId });
        }

        var lane = await _lanes.GetAsync(laneId, cancellationToken).ConfigureAwait(false)
                   ?? throw new RpcException(RpcErrors.NotFound, "Lane 不存在", new { laneId });
        if (lane.Mode != session.Mode)
        {
            throw new RpcException(
                RpcErrors.InvalidParams, "会话模式与 Lane 不匹配", new { sessionId, laneId });
        }

        await _sessions.SetActiveForLaneAsync(laneId, sessionId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>工作会话消息分页（已归档仍可读）。</summary>
    /// <param name="sessionId">会话 id。</param>
    /// <param name="beforeId">取该 id 之前的一页。</param>
    /// <param name="afterId">取该 id 之后的一页。</param>
    /// <param name="limit">本页条数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<SessionMessagesResult> GetMessagesAsync(
        string sessionId,
        long? beforeId = null,
        long? afterId = null,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        await RequireAsync(sessionId, cancellationToken).ConfigureAwait(false);
        return await ReadPageAsync(sessionId: sessionId, laneId: null, beforeId, afterId, limit, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>RP Lane 历史分页：只读投影，工作 Lane 不走这里。</summary>
    /// <param name="laneId">Lane id。</param>
    /// <param name="beforeId">取该 id 之前的一页。</param>
    /// <param name="afterId">取该 id 之后的一页。</param>
    /// <param name="limit">本页条数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<SessionMessagesResult> GetLaneMessagesAsync(
        string laneId,
        long? beforeId = null,
        long? afterId = null,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var lane = await _lanes.GetAsync(laneId, cancellationToken).ConfigureAwait(false)
                   ?? throw new RpcException(RpcErrors.NotFound, "Lane 不存在", new { laneId });
        if (lane.Mode != Mode.Roleplay)
        {
            throw new RpcException(
                RpcErrors.InvalidParams, "工作模式请用 session.messages", new { laneId });
        }

        return await ReadPageAsync(sessionId: null, laneId, beforeId, afterId, limit, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>导出会话（md / json）到 <c>{workspace}/exports/</c>。</summary>
    /// <param name="request">会话 id 与格式。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<SessionExportResult> ExportAsync(
        SessionExportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_workspaceRoot is null)
        {
            throw new RpcException(RpcErrors.Internal, "工作区根目录未接入，无法导出");
        }

        var format = (request.Format ?? "md").Trim().ToLowerInvariant();
        if (format is not ("md" or "json"))
        {
            throw new RpcException(RpcErrors.InvalidParams, "format 只支持 md / json", new { format });
        }

        var session = await RequireAsync(request.SessionId, cancellationToken).ConfigureAwait(false);
        var detail = await ToDetailAsync(session, cancellationToken).ConfigureAwait(false);
        var rows = await ReadAllAsync(session.Id, cancellationToken).ConfigureAwait(false);
        var views = rows.Select(ToView).ToList();

        var directory = Path.Combine(_workspaceRoot, "exports");
        Directory.CreateDirectory(directory);
        var fileName = $"{SafeFileName(session.Title ?? session.Id)}-{session.Id}-{DateTime.Now:yyyyMMdd-HHmmss}.{format}";
        var path = Path.Combine(directory, fileName);

        var content = format == "json"
            ? JsonSerializer.Serialize(
                new { exportedAt = DateTimeOffset.UtcNow, session = detail, messages = views },
                JsonWire.Options)
            : BuildMarkdown(detail, views);

        await File.WriteAllTextAsync(path, content, new UTF8Encoding(false), cancellationToken)
            .ConfigureAwait(false);
        return new SessionExportResult(path);
    }

    private async Task<List<MessageRecord>> ReadAllAsync(string sessionId, CancellationToken cancellationToken)
    {
        var all = new List<MessageRecord>();
        long? afterId = null;
        while (true)
        {
            var page = await _messages.GetPageAsync(
                    sessionId: sessionId,
                    afterId: afterId,
                    limit: MaxPageSize,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (page.Count == 0)
            {
                break;
            }

            all.AddRange(page);
            if (page.Count < MaxPageSize)
            {
                break;
            }

            afterId = page[^1].Id;
        }

        return all;
    }

    private static string BuildMarkdown(SessionDetail session, IReadOnlyList<MessageView> messages)
    {
        var builder = new StringBuilder();
        builder.Append("# ").AppendLine(session.Title ?? session.Id).AppendLine();
        builder.Append("- 会话：`").Append(session.Id).AppendLine("`");
        builder.Append("- 模式：").AppendLine(session.Mode.ToString().ToLowerInvariant());
        builder.Append("- 来源：").AppendLine(session.Origin.ToString().ToLowerInvariant());
        if (!string.IsNullOrWhiteSpace(session.LaneId))
        {
            builder.Append("- Lane：`").Append(session.LaneId).AppendLine("`");
        }

        builder.Append("- 创建：").AppendLine(session.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss zzz"));
        builder.Append("- 最后活跃：").AppendLine(session.LastActiveAt.ToString("yyyy-MM-dd HH:mm:ss zzz"));
        builder.Append("- 消息数：").AppendLine(session.MessageCount.ToString()).AppendLine();
        builder.AppendLine("---").AppendLine();

        foreach (var message in messages)
        {
            var role = message.Role switch
            {
                MessageRole.User => "我",
                MessageRole.Assistant => "她",
                MessageRole.Tool => "工具",
                MessageRole.System => "系统",
                _ => "未知",
            };
            if (message.Kind is MessageKind.ToolCall or MessageKind.ToolResult)
            {
                builder.Append("> [").Append(role).Append("] ")
                    .Append(message.ToolName ?? "tool").Append("：")
                    .AppendLine(message.Text.ReplaceLineEndings(" "));
                continue;
            }

            builder.Append("### ").Append(role)
                .Append(" · ").AppendLine(message.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
            builder.AppendLine();
            builder.AppendLine(message.Text);
            if (message.Attachments is { Count: > 0 } attachments)
            {
                foreach (var attachment in attachments)
                {
                    builder.Append("- 附件：").Append(attachment.FileName)
                        .Append("（").Append(attachment.Mime).Append("）`")
                        .Append(attachment.Path).AppendLine("`");
                }
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string SafeFileName(string value)
    {
        var cleaned = new string(value
            .Select(static c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-')
            .ToArray())
            .Trim('-');
        if (cleaned.Length == 0)
        {
            return "session";
        }

        return cleaned.Length > 40 ? cleaned[..40] : cleaned;
    }

    private async Task<SessionMessagesResult> ReadPageAsync(
        string? sessionId,
        string? laneId,
        long? beforeId,
        long? afterId,
        int limit,
        CancellationToken cancellationToken)
    {
        if (beforeId is not null && afterId is not null)
        {
            throw new RpcException(RpcErrors.InvalidParams, "beforeId 与 afterId 不能同时给");
        }

        var size = ClampLimit(limit);
        var rows = await _messages.GetPageAsync(
                laneId: laneId,
                sessionId: sessionId,
                beforeId: beforeId,
                afterId: afterId,
                limit: size + 1,
                cancellationToken)
            .ConfigureAwait(false);

        var hasMore = rows.Count > size;
        var page = hasMore
            // 默认页和 beforeId 页都是「从尾部取一页」，多出来的那条是最旧的一条，丢掉；
            // afterId 页是从前往后，多出来的是最新的一条，留在下一页。
            ? afterId is null ? rows.Skip(rows.Count - size).ToList() : rows.Take(size).ToList()
            : rows;
        return new SessionMessagesResult(page.Select(ToView).ToList(), hasMore);
    }

    private async Task<SessionDetail> ToDetailAsync(SessionRecord session, CancellationToken cancellationToken)
    {
        var count = await _messages
            .CountAsync(sessionId: session.Id, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var active = session.LaneId is null
            ? null
            : await _sessions.FindActiveAsync(session.LaneId, cancellationToken).ConfigureAwait(false);

        return new SessionDetail(
            session.Id,
            session.Mode,
            session.Origin,
            session.LaneId,
            session.Title,
            session.Archived,
            session.CreatedAt,
            session.LastActiveAt,
            count,
            session.SessionKey,
            session.ParentSessionId,
            active?.Id == session.Id);
    }

    private async Task<SessionSummary> ToSummaryAsync(SessionRecord session, CancellationToken cancellationToken)
    {
        var count = await _messages
            .CountAsync(sessionId: session.Id, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return new SessionSummary(
            session.Id,
            session.Mode,
            session.Origin,
            session.LaneId,
            session.Title,
            session.Archived,
            session.CreatedAt,
            session.LastActiveAt,
            count);
    }

    private async Task<SessionRecord> RequireAsync(string sessionId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new RpcException(RpcErrors.InvalidParams, "sessionId 不能为空");
        }

        return await _sessions.GetAsync(sessionId, cancellationToken).ConfigureAwait(false)
               ?? throw new RpcException(RpcErrors.NotFound, "会话不存在", new { sessionId });
    }

    private static int ClampLimit(int limit) => Math.Clamp(limit <= 0 ? 50 : limit, 1, MaxPageSize);

    private static MessageView ToView(MessageRecord row) => new(
        row.Id,
        row.RoundNo,
        ParseRole(row.Role),
        row.Kind,
        MessageDisplay.Clean(row.Content),
        row.ToolName,
        row.CreatedAt,
        row.Attachments,
        row.Thinking,
        row.ToolCallId,
        row.ToolArguments,
        row.ToolResult);

    /// <summary>框架 <c>ChatRole.Value</c> 是字符串，认不出的落到 Unknown，不丢行。</summary>
    private static MessageRole ParseRole(string value) => value.ToLowerInvariant() switch
    {
        "user" => MessageRole.User,
        "assistant" => MessageRole.Assistant,
        "system" => MessageRole.System,
        "tool" => MessageRole.Tool,
        _ => MessageRole.Unknown,
    };
}
