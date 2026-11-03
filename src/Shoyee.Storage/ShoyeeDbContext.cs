using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Shoyee.Storage.Entities.Channel;
using Shoyee.Storage.Entities.Chat;
using Shoyee.Storage.Entities.Ops;
using Shoyee.Storage.Entities.Session;

namespace Shoyee.Storage;

/// <summary>
/// 单库上下文（<c>{workspace}/state.db</c>，WAL）。配置留文件，这里只装会增长/要查询的流水。
/// FTS5 虚表不映射成实体，走 <c>Search</c> 下的原生 SQL。
/// 枚举一律 <c>HasConversion&lt;string&gt;()</c>，库里存的是 PascalCase 枚举名——原生 SQL 比较时参数别写小写。
/// </summary>
public sealed class ShoyeeDbContext : DbContext
{
    /// <summary>创建上下文。</summary>
    public ShoyeeDbContext(DbContextOptions<ShoyeeDbContext> options)
        : base(options)
    {
    }

    /// <summary>Lane（渠道账号 / 桌面）。</summary>
    public DbSet<LaneEntity> Lanes => Set<LaneEntity>();

    /// <summary>会话。</summary>
    public DbSet<SessionEntity> Sessions => Set<SessionEntity>();

    /// <summary>RP 轮次（真源）。</summary>
    public DbSet<RoundEntity> Rounds => Set<RoundEntity>();

    /// <summary>消息投影（检索/列表/导出）。</summary>
    public DbSet<MessageEntity> Messages => Set<MessageEntity>();

    /// <summary>框架会话状态。</summary>
    public DbSet<SessionStateEntity> SessionStates => Set<SessionStateEntity>();

    /// <summary>RP Lane 摘要。</summary>
    public DbSet<SummaryEntity> Summaries => Set<SummaryEntity>();

    /// <summary>INDEX 条目。</summary>
    public DbSet<IndexEntryEntity> IndexEntries => Set<IndexEntryEntity>();

    /// <summary>通用状态（键值）。</summary>
    public DbSet<StateEntity> States => Set<StateEntity>();

    /// <summary>用量。</summary>
    public DbSet<UsageEntity> Usages => Set<UsageEntity>();

    /// <summary>审批记录。</summary>
    public DbSet<ApprovalEntity> Approvals => Set<ApprovalEntity>();

    /// <summary>投递台账。</summary>
    public DbSet<DeliveryEntity> Deliveries => Set<DeliveryEntity>();

    /// <summary>发送实测。</summary>
    public DbSet<SendAttemptEntity> SendAttempts => Set<SendAttemptEntity>();

    /// <summary>cron 任务。</summary>
    public DbSet<CronJobEntity> CronJobs => Set<CronJobEntity>();

    /// <summary>cron 执行台账。</summary>
    public DbSet<CronExecutionEntity> CronExecutions => Set<CronExecutionEntity>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ShoyeeDbContext).Assembly);
        ApplyUtcTicksForDateTimeOffset(modelBuilder);
    }

    /// <summary>
    /// SQLite 不支持对 DateTimeOffset 排序/比较（EF 会直接抛 NotSupportedException）。
    /// 全库统一转成 UTC ticks（long）存储：既能排序比较，也不丢时序。
    /// </summary>
    private static void ApplyUtcTicksForDateTimeOffset(ModelBuilder modelBuilder)
    {
        var nonNullable = new ValueConverter<DateTimeOffset, long>(
            v => v.UtcTicks,
            v => new DateTimeOffset(v, TimeSpan.Zero));
        var nullable = new ValueConverter<DateTimeOffset?, long?>(
            v => v == null ? null : v.Value.UtcTicks,
            v => v == null ? null : new DateTimeOffset(v.Value, TimeSpan.Zero));

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTimeOffset))
                {
                    property.SetValueConverter(nonNullable);
                }
                else if (property.ClrType == typeof(DateTimeOffset?))
                {
                    property.SetValueConverter(nullable);
                }
            }
        }
    }
}
