using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;
using Shoyee.Storage.Converters;
using Shoyee.Storage.Entities.Chat;
using Shoyee.Storage;
using Shoyee.Storage.Abstractions;
using Shoyee.Storage.Sqlite.Infrastructure;

namespace Shoyee.Storage.Sqlite.Repositories;

/// <summary>
/// 消息投影的读写：检索、桌面列表、导出都吃这张表。
/// 真源不是它——RP 在 <c>rounds</c>，工作模式在会话状态；这里的数据可从真源重建。
/// </summary>
public sealed class MessageRepository : IMessageRepository
{
    private readonly IDbContextFactory<ShoyeeDbContext> _contextFactory;
    private readonly StoragePaths _paths;

    /// <summary>创建投影仓库。</summary>
    public MessageRepository(IDbContextFactory<ShoyeeDbContext> contextFactory, StoragePaths paths)
    {
        _contextFactory = contextFactory;
        _paths = paths;
    }

    /// <summary>追加一条消息投影（工作模式的转录投影用）。不可见/无内容的消息不落行。</summary>
    public async Task<MessageRecord?> AppendAsync(
        string laneId,
        string? sessionId,
        int? roundNo,
        ChatMessage message,
        DateTimeOffset? at = null,
        CancellationToken cancellationToken = default)
    {
        var projection = ChatMessageProjector.Project(message);
        if (!ChatMessageProjector.ShouldPersist(projection))
        {
            return null;
        }

        FtsSchema.Ensure(_paths.DatabasePath);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var entity = new MessageEntity
        {
            LaneId = laneId,
            SessionId = sessionId,
            RoundNo = roundNo,
            Role = projection.Role,
            Kind = projection.Kind,
            Content = projection.Content,
            ToolName = projection.ToolName,
            AttachmentsJson = AttachmentCodec.Serialize(projection.Attachments),
            Thinking = projection.Thinking,
            ToolCallId = projection.ToolCallId,
            ToolArguments = projection.ToolArguments,
            ToolResult = projection.ToolResult,
            CreatedAt = at ?? DateTimeOffset.UtcNow,
        };

        context.Messages.Add(entity);
        await context.SaveChangesAsync(cancellationToken);
        await InsertFtsAsync(context, projection, entity.Id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToRecord(entity);
    }

    /// <summary>
    /// 批量追加工作模式转录投影：一个事务插入全部行并同步 FTS。
    /// <paramref name="startIndex"/> 对应第一条消息的历史下标，逐条递增写到 <c>source_index</c>，
    /// 唯一索引保证同一会话同一下标不会出现第二行。
    /// </summary>
    public async Task<IReadOnlyList<MessageRecord>> AppendRangeAsync(
        string laneId,
        string sessionId,
        IReadOnlyList<ChatMessage> messages,
        int startIndex,
        DateTimeOffset? at = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(laneId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(messages);
        if (messages.Count == 0)
        {
            return [];
        }

        FtsSchema.Ensure(_paths.DatabasePath);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var now = at ?? DateTimeOffset.UtcNow;
        var rows = new List<(MessageProjection Projection, MessageEntity Entity)>();
        for (var i = 0; i < messages.Count; i++)
        {
            var projection = ChatMessageProjector.Project(messages[i]);
            if (!ChatMessageProjector.ShouldPersist(projection))
            {
                continue;
            }

            var entity = new MessageEntity
            {
                LaneId = laneId,
                SessionId = sessionId,
                SourceIndex = startIndex + i,
                Role = projection.Role,
                Kind = projection.Kind,
                Content = projection.Content,
                ToolName = projection.ToolName,
                AttachmentsJson = AttachmentCodec.Serialize(projection.Attachments),
                Thinking = projection.Thinking,
                ToolCallId = projection.ToolCallId,
                ToolArguments = projection.ToolArguments,
                ToolResult = projection.ToolResult,
                CreatedAt = now,
            };
            context.Messages.Add(entity);
            rows.Add((projection, entity));
        }

        if (rows.Count == 0)
        {
            return [];
        }

        await context.SaveChangesAsync(cancellationToken);
        foreach (var (projection, entity) in rows)
        {
            await InsertFtsAsync(context, projection, entity.Id, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return rows.Select(static row => ToRecord(row.Entity)).ToList();
    }

    /// <summary>工作模式已抄到的历史下标 + 1；没有投影则为 0。</summary>
    public async Task<int> GetProjectedIndexAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var max = await context.Messages
            .Where(m => m.SessionId == sessionId && m.SourceIndex != null)
            .MaxAsync(m => (int?)m.SourceIndex, cancellationToken);
        return max is null ? 0 : max.Value + 1;
    }

    /// <summary>把异步结果附件合并到工具结果投影；按 Lane + 工具返回文本里的标记定位。</summary>
    public async Task<MessageRecord?> AttachToToolResultAsync(
        string laneId,
        string resultMarker,
        IReadOnlyList<MessageAttachment> attachments,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(laneId);
        ArgumentException.ThrowIfNullOrWhiteSpace(resultMarker);
        ArgumentNullException.ThrowIfNull(attachments);
        if (attachments.Count == 0)
        {
            return null;
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.Messages
            .Where(m => m.LaneId == laneId
                        && m.Kind == MessageKind.ToolResult
                        && m.ToolResult != null
                        && m.ToolResult.Contains(resultMarker))
            .OrderByDescending(m => m.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return null;
        }

        var merged = (AttachmentCodec.Deserialize(entity.AttachmentsJson) ?? []).ToList();
        var existingPaths = merged.Select(static a => a.Path).ToHashSet(StringComparer.Ordinal);
        foreach (var attachment in attachments)
        {
            if (existingPaths.Add(attachment.Path))
            {
                merged.Add(attachment);
            }
        }

        entity.AttachmentsJson = AttachmentCodec.Serialize(merged);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToRecord(entity);
    }

    /// <summary>最后一条可见助手正文（工具调用不算）；可按 Lane / 会话 / 轮次过滤。</summary>
    public async Task<MessageRecord?> GetLastAssistantAsync(        string? laneId = null,
        string? sessionId = null,
        int? roundNo = null,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var query = context.Messages.AsNoTracking()
            .Where(m => m.Role == "assistant"
                        && m.Content != ""
                        && (m.Kind == MessageKind.Text || m.Kind == MessageKind.Spoken));

        if (laneId is not null)
        {
            query = query.Where(m => m.LaneId == laneId);
        }

        if (sessionId is not null)
        {
            query = query.Where(m => m.SessionId == sessionId);
        }

        if (roundNo is not null)
        {
            query = query.Where(m => m.RoundNo == roundNo);
        }

        var row = await query.OrderByDescending(m => m.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : ToRecord(row);
    }

    /// <summary>删除某会话的全部投影及其 FTS 行；用于从会话状态整段重建。</summary>
    public async Task<int> DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        FtsSchema.Ensure(_paths.DatabasePath);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        // messages_fts 是虚表，没有外键；必须先按 message_id 显式删，否则留下孤儿索引
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM messages_fts WHERE message_id IN (SELECT id FROM messages WHERE session_id = {sessionId});",
            cancellationToken);
        var deleted = await context.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM messages WHERE session_id = {sessionId};",
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return deleted;
    }

    /// <summary>分页取消息（按 id 升序）。<paramref name="afterId"/> 与 <paramref name="beforeId"/> 互斥。</summary>
    /// <summary>按 id 取单条投影（语音合成按消息 id 读正文用）。</summary>
    public async Task<MessageRecord?> GetAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.Messages.AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == id, cancellationToken)
            .ConfigureAwait(false);
        return entity is null ? null : ToRecord(entity);
    }

    public async Task<IReadOnlyList<MessageRecord>> GetPageAsync(
        string? laneId = null,
        string? sessionId = null,
        long? beforeId = null,
        long? afterId = null,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var query = context.Messages.AsNoTracking();
        if (laneId is not null)
        {
            query = query.Where(m => m.LaneId == laneId);
        }

        if (sessionId is not null)
        {
            query = query.Where(m => m.SessionId == sessionId);
        }

        if (afterId is { } after)
        {
            var rows = await query.Where(m => m.Id > after)
                .OrderBy(m => m.Id)
                .Take(limit)
                .ToListAsync(cancellationToken);
            return rows.Select(ToRecord).ToList();
        }

        if (beforeId is { } before)
        {
            var rows = await query.Where(m => m.Id < before)
                .OrderByDescending(m => m.Id)
                .Take(limit)
                .ToListAsync(cancellationToken);
            rows.Reverse();
            return rows.Select(ToRecord).ToList();
        }

        var latest = await query.OrderByDescending(m => m.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
        latest.Reverse();
        return latest.Select(ToRecord).ToList();
    }

    /// <summary>统计条数（按 Lane 或会话）。</summary>
    public async Task<int> CountAsync(
        string? laneId = null,
        string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var query = context.Messages.AsNoTracking();
        if (laneId is not null)
        {
            query = query.Where(m => m.LaneId == laneId);
        }

        if (sessionId is not null)
        {
            query = query.Where(m => m.SessionId == sessionId);
        }

        return await query.CountAsync(cancellationToken);
    }

    private static MessageRecord ToRecord(MessageEntity row) => new(
        row.Id,
        row.LaneId,
        row.SessionId,
        row.RoundNo,
        row.Role,
        row.Kind,
        row.Content,
        row.ToolName,
        row.CreatedAt,
        AttachmentCodec.Deserialize(row.AttachmentsJson),
        row.Thinking,
        row.ToolCallId,
        row.ToolArguments,
        row.ToolResult);

    /// <summary>把一条消息的正文写进检索索引（调用方负责事务）。</summary>
    internal static async Task InsertFtsAsync(
        ShoyeeDbContext context,
        MessageProjection projection,
        long messageId,
        CancellationToken cancellationToken)
    {
        if (!ChatMessageProjector.IsSearchable(projection))
        {
            return;
        }

        await context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO messages_fts (content, message_id) VALUES ({projection.Content}, {messageId});",
            cancellationToken);
    }
}
