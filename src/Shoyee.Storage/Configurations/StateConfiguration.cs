using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shoyee.Storage.Entities.Ops;

namespace Shoyee.Storage.Configurations;

/// <summary>通用状态映射。</summary>
public sealed class StateConfiguration : IEntityTypeConfiguration<StateEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<StateEntity> builder)
    {
        builder.ToTable("states");
        builder.HasKey(x => x.Key);
        builder.Property(x => x.Key).HasColumnName("key");
        builder.Property(x => x.Name).HasColumnName("name");
        builder.Property(x => x.Value).HasColumnName("value");
        builder.Property(x => x.Description).HasColumnName("description");
        builder.Property(x => x.AgentEditable).HasColumnName("agent_editable");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
    }
}
