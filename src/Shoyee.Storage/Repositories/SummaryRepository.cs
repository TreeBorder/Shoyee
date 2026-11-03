using Microsoft.EntityFrameworkCore;
using Shoyee.Storage.Abstractions;
using Shoyee.Storage.Entities.Chat;

namespace Shoyee.Storage.Repositories;

/// <summary>RP 摘要仓库（纯 EF，全局一份累积摘要）。</summary>
public sealed class SummaryRepository : ISummaryRepository
{
    private const string Scope = "rp";
    private const string TargetId = "roleplay";

    private readonly IDbContextFactory<ShoyeeDbContext> _contextFactory;

    /// <summary>创建仓库。</summary>
    public SummaryRepository(IDbContextFactory<ShoyeeDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    /// <inheritdoc />
    public async Task<SummaryRecord?> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await context.Summaries.AsNoTracking()
            .Where(x => x.Scope == Scope && x.TargetId == TargetId)
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : ToRecord(row);
    }

    /// <inheritdoc />
    public async Task<SummaryRecord> AppendAsync(
        int throughRound,
        int roundCount,
        string content,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var row = new SummaryEntity
        {
            Scope = Scope,
            TargetId = TargetId,
            ThroughRound = throughRound,
            RoundCount = roundCount,
            Content = content,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        context.Summaries.Add(row);
        await context.SaveChangesAsync(cancellationToken);
        return ToRecord(row);
    }

    private static SummaryRecord ToRecord(SummaryEntity row) => new(
        row.Id,
        row.ThroughRound,
        row.RoundCount,
        row.Content,
        row.CreatedAt);
}
