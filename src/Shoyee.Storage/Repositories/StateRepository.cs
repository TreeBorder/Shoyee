using Microsoft.EntityFrameworkCore;
using Shoyee.Storage.Abstractions;
using Shoyee.Storage.Entities.Ops;

namespace Shoyee.Storage.Repositories;

/// <summary>通用状态仓库（纯 EF，与提供方无关）。</summary>
public sealed class StateRepository : IStateRepository
{
    private readonly IDbContextFactory<ShoyeeDbContext> _contextFactory;

    /// <summary>创建仓库。</summary>
    public StateRepository(IDbContextFactory<ShoyeeDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StateRecord>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await context.States.AsNoTracking().OrderBy(x => x.Key).ToListAsync(cancellationToken);
        return rows.Select(ToRecord).ToList();
    }

    /// <inheritdoc />
    public async Task<StateRecord?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await context.States.AsNoTracking().FirstOrDefaultAsync(x => x.Key == key, cancellationToken);
        return row is null ? null : ToRecord(row);
    }

    /// <inheritdoc />
    public async Task<bool> SetAsync(
        string key,
        string value,
        bool byAgent = false,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await context.States.FirstOrDefaultAsync(x => x.Key == key, cancellationToken);
        if (row is null || (byAgent && !row.AgentEditable))
        {
            return false;
        }

        row.Value = value;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public async Task<StateRecord> UpsertAsync(StateRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(record.Key);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await context.States.FirstOrDefaultAsync(x => x.Key == record.Key, cancellationToken);
        if (row is null)
        {
            row = new StateEntity { Key = record.Key };
            context.States.Add(row);
        }

        row.Name = record.Name;
        row.Value = record.Value;
        row.Description = record.Description;
        row.AgentEditable = record.AgentEditable;
        row.UpdatedAt = DateTimeOffset.UtcNow;

        await context.SaveChangesAsync(cancellationToken);
        return ToRecord(row);
    }

    /// <inheritdoc />
    public async Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await context.States.FirstOrDefaultAsync(x => x.Key == key, cancellationToken);
        if (row is null)
        {
            return false;
        }

        context.States.Remove(row);
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public async Task SeedIfEmptyAsync(
        IReadOnlyList<StateRecord> defaults,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        if (await context.States.AnyAsync(cancellationToken))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        context.States.AddRange(defaults.Select(d => new StateEntity
        {
            Key = d.Key,
            Name = d.Name,
            Value = d.Value,
            Description = d.Description,
            AgentEditable = d.AgentEditable,
            UpdatedAt = now,
        }));
        await context.SaveChangesAsync(cancellationToken);
    }

    private static StateRecord ToRecord(StateEntity row) => new(
        row.Key,
        row.Name,
        row.Value,
        row.Description,
        row.AgentEditable,
        row.UpdatedAt);
}
