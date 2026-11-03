using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Shoyee.Storage.Abstractions;
using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Rpc;
using Shoyee.Storage.Entities.Ops;

namespace Shoyee.Storage.Repositories;

/// <summary>INDEX 地址簿的 SQLite 实现。</summary>
public sealed class IndexRepository : IIndexRepository
{
    private readonly IDbContextFactory<ShoyeeDbContext> _contextFactory;

    /// <summary>创建实现。</summary>
    public IndexRepository(IDbContextFactory<ShoyeeDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IndexEntry>> ListAsync(
        string? scope = null,
        string? query = null,
        bool enabledOnly = false,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var rows = context.IndexEntries.AsNoTracking();

        if (enabledOnly)
        {
            rows = rows.Where(x => x.Enabled);
        }

        if (!string.IsNullOrWhiteSpace(scope) && !string.Equals(scope, "all", StringComparison.OrdinalIgnoreCase))
        {
            rows = rows.Where(x => x.Scope == "all" || x.Scope == scope);
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            var like = "%" + query.Trim() + "%";
            rows = rows.Where(x => EF.Functions.Like(x.Key, like)
                                   || EF.Functions.Like(x.Title, like)
                                   || EF.Functions.Like(x.Value, like)
                                   || (x.Notes != null && EF.Functions.Like(x.Notes, like)));
        }

        var list = await rows.OrderByDescending(x => x.Pinned)
            .ThenBy(x => x.Title)
            .ToListAsync(cancellationToken);
        return list.Select(ToDto).ToList();
    }

    /// <inheritdoc />
    public async Task<IndexEntry> UpsertAsync(IndexEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.Key);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var row = await context.IndexEntries.FirstOrDefaultAsync(x => x.Key == entry.Key, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        if (row is null)
        {
            row = new IndexEntryEntity
            {
                Key = entry.Key,
                CreatedAt = entry.CreatedAt == default ? now : entry.CreatedAt,
            };
            context.IndexEntries.Add(row);
        }

        row.Title = entry.Title;
        row.Kind = entry.Kind;
        row.Value = entry.Value;
        row.Scope = string.IsNullOrWhiteSpace(entry.Scope) ? "all" : entry.Scope;
        row.TagsJson = entry.Tags is { Count: > 0 } ? JsonSerializer.Serialize(entry.Tags, JsonWire.Options) : null;
        row.Notes = entry.Notes;
        row.Pinned = entry.Pinned;
        row.Enabled = entry.Enabled;
        row.UpdatedAt = now;

        await context.SaveChangesAsync(cancellationToken);
        return ToDto(row);
    }

    /// <inheritdoc />
    public async Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await context.IndexEntries.FirstOrDefaultAsync(x => x.Key == key, cancellationToken);
        if (row is null)
        {
            return false;
        }

        context.IndexEntries.Remove(row);
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static IndexEntry ToDto(IndexEntryEntity row)
    {
        IReadOnlyList<string>? tags = null;
        if (!string.IsNullOrWhiteSpace(row.TagsJson))
        {
            tags = JsonSerializer.Deserialize<List<string>>(row.TagsJson, JsonWire.Options);
        }

        return new IndexEntry(
            Id: row.Id,
            Key: row.Key,
            Title: row.Title,
            Kind: row.Kind,
            Value: row.Value,
            Scope: row.Scope,
            Tags: tags,
            Notes: row.Notes,
            Pinned: row.Pinned,
            CreatedAt: row.CreatedAt,
            UpdatedAt: row.UpdatedAt,
            Enabled: row.Enabled);
    }
}
