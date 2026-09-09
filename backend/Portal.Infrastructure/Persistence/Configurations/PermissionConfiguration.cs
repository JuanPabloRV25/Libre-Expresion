using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portal.Domain.Permissions;

namespace Portal.Infrastructure.Persistence.Configurations;

internal sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("Permissions");

        builder.HasKey(permission => permission.Id);

        builder.Property(permission => permission.Code)
            .HasColumnType("varchar(150)")
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(permission => permission.Module)
            .HasColumnType("varchar(100)")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(permission => permission.Action)
            .HasColumnType("varchar(100)")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(permission => permission.DisplayName)
            .HasColumnType("varchar(150)")
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(permission => permission.Description)
            .HasColumnType("varchar(500)")
            .HasMaxLength(500);

        builder.Property(permission => permission.IsActive)
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(permission => permission.CreatedAt)
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(permission => permission.UpdatedAt)
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.HasIndex(permission => permission.Code)
            .IsUnique();

        builder.HasIndex(permission => permission.Module);
        builder.HasIndex(permission => permission.IsActive);
    }
}
