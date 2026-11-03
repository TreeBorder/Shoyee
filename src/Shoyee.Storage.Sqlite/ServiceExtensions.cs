using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shoyee.Contracts.Options;
using Shoyee.Storage;
using Shoyee.Storage.Abstractions;
using Shoyee.Storage.Repositories;
using Shoyee.Storage.Sqlite.Infrastructure;
using Shoyee.Storage.Sqlite.Repositories;

namespace Shoyee.Storage.Sqlite;

/// <summary>SQLite 存储注册。数据库提供方的选择在 Daemon 组合根，不在这里。</summary>
public static class ServiceExtensions
{
    /// <summary>注册 SQLite 存储与全部仓储实现。</summary>
    public static IServiceCollection AddShoyeeSqliteStorage(this IServiceCollection services, WorkspaceConfig config)
    {
        var paths = new StoragePaths(config.ResolveDatabasePath());
        var options = StorageOptions.ForSqlite(paths.DatabasePath);
        services.AddSingleton(options);
        services.AddSingleton(paths);
        services.AddDbContextFactory<ShoyeeDbContext>(o => o.UseSqlite(
            options.ConnectionString,
            // 迁移产物在本项目里，不指 MigrationsAssembly 的话 dotnet ef 会报 target/migrations 不匹配
            b => b.MigrationsAssembly(typeof(ServiceExtensions).Assembly.GetName().Name)));

        services.AddSingleton<IIndexRepository, IndexRepository>();
        services.AddSingleton<IStateRepository, StateRepository>();
        services.AddSingleton<ILaneRepository, LaneRepository>();
        services.AddSingleton<ISessionRepository, SessionRepository>();
        services.AddSingleton<ISessionStateRepository, SessionStateRepository>();
        services.AddSingleton<ISummaryRepository, SummaryRepository>();
        services.AddSingleton<IUsageRepository, UsageRepository>();
        // 以下四个为预置实现（暂无调用方，接口可能调整）：
        services.AddSingleton<IApprovalRepository, ApprovalRepository>();
        services.AddSingleton<IDeliveryRepository, DeliveryRepository>();
        services.AddSingleton<ISendAttemptRepository, SendAttemptRepository>();
        services.AddSingleton<ICronRepository, CronRepository>();
        services.AddSingleton<IStorageInitializer, StorageInitializer>();
        services.AddSingleton<IRoundRepository, RoundRepository>();
        services.AddSingleton<IMessageRepository, MessageRepository>();
        services.AddSingleton<ISearchRepository, SearchRepository>();
        return services;
    }
}
