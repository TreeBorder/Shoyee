using Microsoft.EntityFrameworkCore;
using Shoyee.Storage.Abstractions;
using Shoyee.Storage.Entities.Session;

namespace Shoyee.Storage.Repositories;

/// <summary>框架会话状态仓库（原文存取）。</summary>
public sealed class SessionStateRepository : ISessionStateRepository
{
    private readonly IDbContextFactory<ShoyeeDbContext> _contextFactory;

    /// <summary>创建仓库。</summary>
    public SessionStateRepository(IDbContextFactory<ShoyeeDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    /// <inheritdoc />
    public async Task<string?> GetAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.SessionStates.AsNoTracking()
            .Where(x => x.StorageKey == storageKey)
            .Select(x => x.StateJson)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task SaveAsync(
        string storageKey,
        string stateJson,
        DateTimeOffset? at = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await context.SessionStates.FirstOrDefaultAsync(x => x.StorageKey == storageKey, cancellationToken);
        if (row is null)
        {
            context.SessionStates.Add(new SessionStateEntity
            {
                StorageKey = storageKey,
                StateJson = stateJson,
                UpdatedAt = at ?? DateTimeOffset.UtcNow,
            });
        }
        else
        {
            row.StateJson = stateJson;
            row.UpdatedAt = at ?? DateTimeOffset.UtcNow;
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await context.SessionStates.FirstOrDefaultAsync(x => x.StorageKey == storageKey, cancellationToken);
        if (row is null)
        {
            return false;
        }

        context.SessionStates.Remove(row);
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }
}
