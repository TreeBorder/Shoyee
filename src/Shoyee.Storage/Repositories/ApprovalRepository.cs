using Microsoft.EntityFrameworkCore;
using Shoyee.Contracts.Domain;
using Shoyee.Storage.Abstractions;
using Shoyee.Storage.Entities.Ops;

namespace Shoyee.Storage.Repositories;

/// <summary>审批记录仓库（纯 EF）。</summary>
public sealed class ApprovalRepository : IApprovalRepository
{
    private readonly IDbContextFactory<ShoyeeDbContext> _contextFactory;

    /// <summary>创建仓库。</summary>
    public ApprovalRepository(IDbContextFactory<ShoyeeDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    /// <inheritdoc />
    public async Task AddAsync(ApprovalRecord record, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        context.Approvals.Add(new ApprovalEntity
        {
            RequestId = record.RequestId,
            SessionId = record.SessionId,
            TurnId = record.TurnId,
            ToolName = record.ToolName,
            Arguments = record.ArgumentsJson,
            Decision = record.Decision,
            DecidedBy = record.DecidedBy,
            DecidedAt = record.DecidedAt,
            CreatedAt = record.CreatedAt == default ? DateTimeOffset.UtcNow : record.CreatedAt,
        });
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ApprovalRecord?> GetAsync(string requestId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await context.Approvals.AsNoTracking()
            .FirstOrDefaultAsync(x => x.RequestId == requestId, cancellationToken);
        return row is null ? null : ToRecord(row);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ApprovalRecord>> GetPendingAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await context.Approvals.AsNoTracking()
            .Where(x => x.SessionId == sessionId && x.Decision == null)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
        return rows.Select(ToRecord).ToList();
    }

    /// <inheritdoc />
    public async Task<bool> ResolveAsync(
        string requestId,
        ApprovalDecision decision,
        string decidedBy,
        DateTimeOffset? at = null,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await context.Approvals.FirstOrDefaultAsync(x => x.RequestId == requestId, cancellationToken);
        if (row is null)
        {
            return false;
        }

        row.Decision = decision;
        row.DecidedBy = decidedBy;
        row.DecidedAt = at ?? DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public async Task<int> ExpireBeforeAsync(DateTimeOffset before, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await context.Approvals
            .Where(x => x.Decision == null && x.CreatedAt < before)
            .ToListAsync(cancellationToken);

        foreach (var row in rows)
        {
            row.Decision = ApprovalDecision.Expired;
            row.DecidedBy = "timeout";
            row.DecidedAt = DateTimeOffset.UtcNow;
        }

        if (rows.Count > 0)
        {
            await context.SaveChangesAsync(cancellationToken);
        }

        return rows.Count;
    }

    private static ApprovalRecord ToRecord(ApprovalEntity row) => new(
        row.RequestId,
        row.SessionId,
        row.TurnId,
        row.ToolName,
        row.Arguments,
        row.Decision,
        row.DecidedBy,
        row.DecidedAt,
        row.CreatedAt);
}
