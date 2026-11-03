using Microsoft.EntityFrameworkCore;
using Shoyee.Storage.Abstractions;
using Shoyee.Storage.Entities.Ops;

namespace Shoyee.Storage.Repositories;

/// <summary>用量仓库（纯 EF）。</summary>
public sealed class UsageRepository : IUsageRepository
{
    private readonly IDbContextFactory<ShoyeeDbContext> _contextFactory;

    /// <summary>创建仓库。</summary>
    public UsageRepository(IDbContextFactory<ShoyeeDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    /// <inheritdoc />
    public async Task AddAsync(
        string? sessionId,
        string? turnId,
        string purpose,
        string model,
        int inputTokens,
        int outputTokens,
        int cacheHitTokens,
        int cacheMissTokens,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        context.Usages.Add(new UsageEntity
        {
            SessionId = sessionId,
            TurnId = turnId,
            Purpose = purpose,
            Model = model,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            CacheHitTokens = cacheHitTokens,
            CacheMissTokens = cacheMissTokens,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<UsageTotals> GetTotalsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var query = context.Usages.AsNoTracking()
            .Where(x => x.CreatedAt >= from && x.CreatedAt <= to);
        if (sessionId is not null)
        {
            query = query.Where(x => x.SessionId == sessionId);
        }

        var rows = await query.ToListAsync(cancellationToken);

        return new UsageTotals(
            rows.Count,
            rows.Sum(x => x.InputTokens),
            rows.Sum(x => x.OutputTokens),
            rows.Sum(x => x.CacheHitTokens),
            rows.Sum(x => x.CacheMissTokens));
    }

    /// <inheritdoc />
    public async Task<UsageCall?> GetLastCallAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await context.Usages.AsNoTracking()
            .Where(x => x.SessionId == sessionId && x.Purpose == "chat.call")
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return row is null
            ? null
            : new UsageCall(row.InputTokens, row.OutputTokens, row.CacheHitTokens, row.CreatedAt);
    }
}
