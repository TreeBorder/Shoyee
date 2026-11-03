using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shoyee.Storage.Entities.Ops;

namespace Shoyee.Storage.Configurations;

/// <summary>INDEX 映射。</summary>
public sealed class IndexEntryConfiguration : IEntityTypeConfiguration<IndexEntryEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<IndexEntryEntity> builder)
    {
        builder.ToTable("index_entries");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(x => x.Key).HasColumnName("key");
        builder.Property(x => x.Title).HasColumnName("title");
        builder.Property(x => x.Kind).HasColumnName("kind");
        builder.Property(x => x.Value).HasColumnName("value");
        builder.Property(x => x.Scope).HasColumnName("scope");
        builder.Property(x => x.TagsJson).HasColumnName("tags");
        builder.Property(x => x.Notes).HasColumnName("notes");
        builder.Property(x => x.Pinned).HasColumnName("pinned");
        builder.Property(x => x.Enabled).HasColumnName("enabled").HasDefaultValue(true);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(x => x.Key).IsUnique();
    }
}

/// <summary>用量映射。</summary>
public sealed class UsageConfiguration : IEntityTypeConfiguration<UsageEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<UsageEntity> builder)
    {
        builder.ToTable("usage");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(x => x.SessionId).HasColumnName("session_id");
        builder.Property(x => x.TurnId).HasColumnName("turn_id");
        builder.Property(x => x.Purpose).HasColumnName("purpose");
        builder.Property(x => x.Model).HasColumnName("model");
        builder.Property(x => x.InputTokens).HasColumnName("input_tokens");
        builder.Property(x => x.OutputTokens).HasColumnName("output_tokens");
        builder.Property(x => x.CacheHitTokens).HasColumnName("cache_hit_tokens");
        builder.Property(x => x.CacheMissTokens).HasColumnName("cache_miss_tokens");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.HasIndex(x => x.CreatedAt);
    }
}

/// <summary>审批映射。</summary>
public sealed class ApprovalConfiguration : IEntityTypeConfiguration<ApprovalEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ApprovalEntity> builder)
    {
        builder.ToTable("approvals");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(x => x.RequestId).HasColumnName("request_id");
        builder.Property(x => x.SessionId).HasColumnName("session_id");
        builder.Property(x => x.TurnId).HasColumnName("turn_id");
        builder.Property(x => x.ToolName).HasColumnName("tool_name");
        builder.Property(x => x.Arguments).HasColumnName("arguments");
        builder.Property(x => x.Decision).HasColumnName("decision").HasConversion<string>();
        builder.Property(x => x.DecidedBy).HasColumnName("decided_by");
        builder.Property(x => x.DecidedAt).HasColumnName("decided_at");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.HasIndex(x => x.RequestId).IsUnique();
        builder.HasIndex(x => new { x.SessionId, x.CreatedAt });
    }
}

/// <summary>cron 任务映射。</summary>
public sealed class CronJobConfiguration : IEntityTypeConfiguration<CronJobEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<CronJobEntity> builder)
    {
        builder.ToTable("cron_jobs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.Name).HasColumnName("name");
        builder.Property(x => x.Schedule).HasColumnName("schedule");
        builder.Property(x => x.Kind).HasColumnName("kind");
        builder.Property(x => x.Payload).HasColumnName("payload");
        builder.Property(x => x.Deliver).HasColumnName("deliver");
        builder.Property(x => x.Mode).HasColumnName("mode").HasConversion<string>();
        builder.Property(x => x.LaneId).HasColumnName("lane_id");
        builder.Property(x => x.SessionId).HasColumnName("session_id");
        builder.Property(x => x.Enabled).HasColumnName("enabled");
        builder.Property(x => x.LastRunAt).HasColumnName("last_run_at");
        builder.Property(x => x.NextRunAt).HasColumnName("next_run_at");
        builder.Property(x => x.LastStatus).HasColumnName("last_status");
        builder.Property(x => x.LastError).HasColumnName("last_error");
        builder.Property(x => x.FailureStreak).HasColumnName("failure_streak");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.HasIndex(x => x.NextRunAt);
    }
}

/// <summary>cron 执行台账映射。</summary>
public sealed class CronExecutionConfiguration : IEntityTypeConfiguration<CronExecutionEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<CronExecutionEntity> builder)
    {
        builder.ToTable("cron_executions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(x => x.JobId).HasColumnName("job_id");
        builder.Property(x => x.ScheduledFor).HasColumnName("scheduled_for");
        builder.Property(x => x.StartedAt).HasColumnName("started_at");
        builder.Property(x => x.FinishedAt).HasColumnName("finished_at");
        builder.Property(x => x.State).HasColumnName("state");
        builder.Property(x => x.Result).HasColumnName("result");
        builder.HasIndex(x => new { x.JobId, x.ScheduledFor }).IsUnique();
    }
}
