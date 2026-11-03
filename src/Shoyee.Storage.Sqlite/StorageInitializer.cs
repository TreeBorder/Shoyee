using Microsoft.EntityFrameworkCore;
using Shoyee.Storage;
using Shoyee.Storage.Abstractions;
using Shoyee.Storage.Sqlite.Infrastructure;

namespace Shoyee.Storage.Sqlite;

/// <summary>启动期存储初始化：跑迁移 + 建 FTS5 虚表。由 Daemon 在启动时调用一次。</summary>
public sealed class StorageInitializer : IStorageInitializer
{
    private readonly IDbContextFactory<ShoyeeDbContext> _contextFactory;
    private readonly StoragePaths _paths;
    private readonly IStateRepository _states;

    /// <summary>创建初始化器。</summary>
    public StorageInitializer(
        IDbContextFactory<ShoyeeDbContext> contextFactory,
        StoragePaths paths,
        IStateRepository states)
    {
        _contextFactory = contextFactory;
        _paths = paths;
        _states = states;
    }

    /// <summary>迁移 + FTS 建表（幂等）。</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await context.Database.MigrateAsync(cancellationToken);
        FtsSchema.Ensure(_paths.DatabasePath);
        await _states.SeedIfEmptyAsync(StateDefaults.All, cancellationToken);
    }
}
