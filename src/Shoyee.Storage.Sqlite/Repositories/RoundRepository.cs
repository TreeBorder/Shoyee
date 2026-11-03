using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Shoyee.Storage.Converters;
using Shoyee.Storage.Entities.Chat;
using Shoyee.Storage;
using Shoyee.Storage.Abstractions;
using Shoyee.Storage.Sqlite.Infrastructure;

namespace Shoyee.Storage.Sqlite.Repositories;

/// <summary>
/// RP 轮次仓库：一行一轮（Harem 的 <c>ChatRawMessage</c> 落到 SQLite）。
/// 写一轮 = 一个事务里插入 rounds 行 + 派生 messages 投影 + FTS；读窗口按全局轮次号取。
///
/// 轮次号跨 Lane 全局单调（RP 只有一条时间线）；并发安全由调用方的 RP 全局 gate 保证，
/// 本类不自带锁。
/// </summary>
public sealed class RoundRepository : IRoundRepository
{
    private readonly IDbContextFactory<ShoyeeDbContext> _contextFactory;
    private readonly StoragePaths _paths;

    /// <summary>创建轮次仓库。</summary>
    public RoundRepository(IDbContextFactory<ShoyeeDbContext> contextFactory, StoragePaths paths)
    {
        _contextFactory = contextFactory;
        _paths = paths;
    }

    /// <summary>追加一轮（轮次号自动 = 全局最大 + 1），同时写投影与 FTS。</summary>
    public async Task<RoundRecord> AppendAsync(
        string laneId,
        IReadOnlyList<ChatMessage> request,
        IReadOnlyList<ChatMessage> response,
        DateTimeOffset? at = null,
        CancellationToken cancellationToken = default)
    {
        FtsSchema.Ensure(_paths.DatabasePath);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var roundNo = (await context.Rounds
            .Select(r => (int?)r.RoundNo)
            .MaxAsync(cancellationToken) ?? 0) + 1;

        var now = at ?? DateTimeOffset.UtcNow;
        context.Rounds.Add(new RoundEntity
        {
            LaneId = laneId,
            RoundNo = roundNo,
            RequestJson = ChatMessageCodec.SerializeList(request),
            ResponseJson = ChatMessageCodec.SerializeList(response),
            CreatedAt = now,
        });

        var projections = new List<(MessageProjection Projection, MessageEntity Entity)>();
        foreach (var message in request.Concat(response))
        {
            var projection = ChatMessageProjector.Project(message);
            if (!ChatMessageProjector.ShouldPersist(projection))
            {
                continue;
            }

            var entity = new MessageEntity
            {
                LaneId = laneId,
                RoundNo = roundNo,
                Role = projection.Role,
                Kind = projection.Kind,
                Content = projection.Content,
                ToolName = projection.ToolName,
                AttachmentsJson = AttachmentCodec.Serialize(projection.Attachments),
                CreatedAt = now,
            };
            context.Messages.Add(entity);
            projections.Add((projection, entity));
        }

        await context.SaveChangesAsync(cancellationToken);
        foreach (var (projection, entity) in projections)
        {
            await MessageRepository.InsertFtsAsync(context, projection, entity.Id, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new RoundRecord(roundNo, laneId, request, response, now);
    }

    /// <summary>取全局最近 <paramref name="count"/> 轮（按轮次号升序返回）。</summary>
    public async Task<IReadOnlyList<RoundRecord>> GetRecentAsync(
        int count = 50,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await context.Rounds.AsNoTracking()
            .OrderByDescending(r => r.RoundNo)
            .Take(count)
            .ToListAsync(cancellationToken);
        rows.Reverse();
        return rows.Select(ToRecord).ToList();
    }

    /// <summary>取全局 <paramref name="afterRoundNo"/> 之后的轮次（升序）。</summary>
    public async Task<IReadOnlyList<RoundRecord>> GetAfterAsync(
        int afterRoundNo,
        int count = 50,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await context.Rounds.AsNoTracking()
            .Where(r => r.RoundNo > afterRoundNo)
            .OrderBy(r => r.RoundNo)
            .Take(count)
            .ToListAsync(cancellationToken);
        return rows.Select(ToRecord).ToList();
    }

    /// <summary>全局最大轮次号（没有则为 0）；摘要触发用它取模。</summary>
    public async Task<int> GetMaxRoundNoAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Rounds
            .Select(r => (int?)r.RoundNo)
            .MaxAsync(cancellationToken) ?? 0;
    }

    /// <summary>RP 全局轮数统计。</summary>
    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Rounds.CountAsync(cancellationToken);
    }

    private static RoundRecord ToRecord(RoundEntity row) => new(
        row.RoundNo,
        row.LaneId,
        ChatMessageCodec.DeserializeList(row.RequestJson),
        ChatMessageCodec.DeserializeList(row.ResponseJson),
        row.CreatedAt);
}
