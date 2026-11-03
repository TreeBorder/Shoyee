using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shoyee.Storage.Entities.Channel;

namespace Shoyee.Storage.Configurations;

/// <summary>Lane 映射。</summary>
public sealed class LaneConfiguration : IEntityTypeConfiguration<LaneEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<LaneEntity> builder)
    {
        builder.ToTable("lanes");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.Channel).HasColumnName("channel").HasConversion<string>();
        builder.Property(x => x.Mode).HasColumnName("mode").HasConversion<string>();
        builder.Property(x => x.ExternalKey).HasColumnName("external_key");
        builder.Property(x => x.DisplayName).HasColumnName("display_name");
        builder.Property(x => x.ActiveSessionId).HasColumnName("active_session_id");
        builder.Property(x => x.LastInboundAt).HasColumnName("last_inbound_at");
        builder.Property(x => x.LastInteractionAt).HasColumnName("last_interaction_at");
        builder.Property(x => x.LastTarget).HasColumnName("last_target");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.HasIndex(x => new { x.Channel, x.ExternalKey, x.Mode }).IsUnique();
    }
}

/// <summary>投递台账映射。</summary>
public sealed class DeliveryConfiguration : IEntityTypeConfiguration<DeliveryEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<DeliveryEntity> builder)
    {
        builder.ToTable("deliveries");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(x => x.LaneId).HasColumnName("lane_id");
        builder.Property(x => x.SessionId).HasColumnName("session_id");
        builder.Property(x => x.Kind).HasColumnName("kind");
        builder.Property(x => x.PayloadJson).HasColumnName("payload_json");
        builder.Property(x => x.State).HasColumnName("state").HasConversion<string>();
        builder.Property(x => x.Attempts).HasColumnName("attempts");
        builder.Property(x => x.NextAttemptAt).HasColumnName("next_attempt_at");
        builder.Property(x => x.LastError).HasColumnName("last_error");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(x => new { x.State, x.UpdatedAt });
    }
}

/// <summary>发送实测映射。</summary>
public sealed class SendAttemptConfiguration : IEntityTypeConfiguration<SendAttemptEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SendAttemptEntity> builder)
    {
        builder.ToTable("send_attempts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(x => x.LaneId).HasColumnName("lane_id");
        builder.Property(x => x.SinceLastInboundSeconds).HasColumnName("since_last_inbound_seconds");
        builder.Property(x => x.Result).HasColumnName("result");
        builder.Property(x => x.Error).HasColumnName("error");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.HasIndex(x => new { x.LaneId, x.CreatedAt });
    }
}
