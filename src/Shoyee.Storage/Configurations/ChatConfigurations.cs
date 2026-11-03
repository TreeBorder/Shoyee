using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shoyee.Storage.Entities.Chat;

namespace Shoyee.Storage.Configurations;

/// <summary>RP 轮次映射（真源）。</summary>
public sealed class RoundConfiguration : IEntityTypeConfiguration<RoundEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<RoundEntity> builder)
    {
        builder.ToTable("rounds");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(x => x.LaneId).HasColumnName("lane_id");
        builder.Property(x => x.RoundNo).HasColumnName("round_no");
        builder.Property(x => x.RequestJson).HasColumnName("request_json");
        builder.Property(x => x.ResponseJson).HasColumnName("response_json");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.HasIndex(x => new { x.LaneId, x.RoundNo }).IsUnique();
    }
}

/// <summary>消息投影映射。</summary>
public sealed class MessageConfiguration : IEntityTypeConfiguration<MessageEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MessageEntity> builder)
    {
        builder.ToTable("messages");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(x => x.LaneId).HasColumnName("lane_id");
        builder.Property(x => x.SessionId).HasColumnName("session_id");
        builder.Property(x => x.RoundNo).HasColumnName("round_no");
        builder.Property(x => x.SourceIndex).HasColumnName("source_index");
        builder.Property(x => x.Role).HasColumnName("role");
        builder.Property(x => x.Kind).HasColumnName("kind").HasConversion<string>();
        builder.Property(x => x.Content).HasColumnName("content");
        builder.Property(x => x.ToolName).HasColumnName("tool_name");
        builder.Property(x => x.AttachmentsJson).HasColumnName("attachments_json");
        builder.Property(x => x.Thinking).HasColumnName("thinking");
        builder.Property(x => x.ToolCallId).HasColumnName("tool_call_id");
        builder.Property(x => x.ToolArguments).HasColumnName("tool_arguments");
        builder.Property(x => x.ToolResult).HasColumnName("tool_result");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.HasIndex(x => new { x.LaneId, x.Id });
        builder.HasIndex(x => new { x.SessionId, x.Id });
        builder.HasIndex(x => new { x.LaneId, x.RoundNo });
        // 工作模式转录去重键；RP 行 session_id/source_index 都为 null，不受约束
        builder.HasIndex(x => new { x.SessionId, x.SourceIndex })
            .IsUnique()
            .HasFilter("session_id IS NOT NULL AND source_index IS NOT NULL");
        builder.HasIndex(x => x.CreatedAt);
    }
}

/// <summary>RP 摘要映射。</summary>
public sealed class SummaryConfiguration : IEntityTypeConfiguration<SummaryEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SummaryEntity> builder)
    {
        builder.ToTable("summaries");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(x => x.Scope).HasColumnName("scope");
        builder.Property(x => x.TargetId).HasColumnName("target_id");
        builder.Property(x => x.ThroughRound).HasColumnName("through_round");
        builder.Property(x => x.RoundCount).HasColumnName("round_count");
        builder.Property(x => x.Content).HasColumnName("content");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.HasIndex(x => new { x.Scope, x.TargetId, x.CreatedAt });
    }
}
