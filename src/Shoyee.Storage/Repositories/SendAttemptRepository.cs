using Microsoft.EntityFrameworkCore;
using Shoyee.Storage.Abstractions;
using Shoyee.Storage.Entities.Channel;

namespace Shoyee.Storage.Repositories;

/// <summary>发送实测仓库（纯 EF）。</summary>
public sealed class SendAttemptRepository : ISendAttemptRepository
{
    private readonly IDbContextFactory<ShoyeeDbContext> _contextFactory;

    /// <summary>创建仓库。</summary>
    public SendAttemptRepository(IDbContextFactory<ShoyeeDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    /// <inheritdoc />
    public async Task AddAsync(SendAttemptRecord record, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        context.SendAttempts.Add(new SendAttemptEntity
        {
            LaneId = record.LaneId,
            SinceLastInboundSeconds = record.SinceLastInboundSeconds,
            Result = record.Result,
            Error = record.Error,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<DateTimeOffset?> GetLastSuccessAsync(string laneId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await context.SendAttempts.AsNoTracking()
            .Where(x => x.LaneId == laneId && x.Result == "ok")
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        return row?.CreatedAt;
    }
}
