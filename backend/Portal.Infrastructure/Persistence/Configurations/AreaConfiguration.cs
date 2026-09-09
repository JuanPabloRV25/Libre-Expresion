using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portal.Domain.Areas;

namespace Portal.Infrastructure.Persistence.Configurations;

internal sealed class AreaConfiguration : IEntityTypeConfiguration<Area>
{
    public void Configure(EntityTypeBuilder<Area> builder)
    {
        builder.ToTable("Areas");

        builder.HasKey(area => area.Id);

        builder.Property(area => area.Name)
            .HasColumnType("varchar(120)")
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(area => area.Description)
            .HasColumnType("varchar(500)")
            .HasMaxLength(500);

        builder.Property(area => area.IsActive)
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(area => area.CreatedAt)
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(area => area.UpdatedAt)
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.HasIndex(area => area.Name)
            .IsUnique();
    }
}
