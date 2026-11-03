using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shoyee.Storage.Entities.Session;

namespace Shoyee.Storage.Configurations;

/// <summary>会话映射。</summary>
public sealed class SessionConfiguration : IEntityTypeConfiguration<SessionEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SessionEntity> builder)
    {
        builder.ToTable("sessions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.Mode).HasColumnName("mode").HasConversion<string>();
        builder.Property(x => x.Origin).HasColumnName("origin").HasConversion<string>();
        builder.Property(x => x.LaneId).HasColumnName("lane_id");
        builder.Property(x => x.SessionKey).HasColumnName("session_key");
        builder.Property(x => x.Title).HasColumnName("title");
        builder.Property(x => x.ParentSessionId).HasColumnName("parent_session_id");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.LastActiveAt).HasColumnName("last_active_at");
        builder.Property(x => x.Archived).HasColumnName("archived");
        builder.HasIndex(x => new { x.SessionKey, x.CreatedAt });
        builder.HasIndex(x => x.LastActiveAt);
        builder.HasOne<Entities.Channel.LaneEntity>().WithMany().HasForeignKey(x => x.LaneId).OnDelete(DeleteBehavior.SetNull);
    }
}

/// <summary>框架会话状态映射（只读写原文，不改写）。</summary>
public sealed class SessionStateConfiguration : IEntityTypeConfiguration<SessionStateEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SessionStateEntity> builder)
    {
        builder.ToTable("session_state");
        builder.HasKey(x => x.StorageKey);
        builder.Property(x => x.StorageKey).HasColumnName("storage_key");
        builder.Property(x => x.StateJson).HasColumnName("state_json");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
    }
}
