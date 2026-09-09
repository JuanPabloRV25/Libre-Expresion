using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portal.Infrastructure.Identity;

namespace Portal.Infrastructure.Persistence.Configurations;

internal sealed class ApplicationRoleConfiguration : IEntityTypeConfiguration<ApplicationRole>
{
    public void Configure(EntityTypeBuilder<ApplicationRole> builder)
    {
        builder.ToTable("AspNetRoles");

        builder.Property(role => role.Name)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(role => role.NormalizedName)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(role => role.Description)
            .HasColumnType("varchar(500)")
            .HasMaxLength(500);

        builder.Property(role => role.IsActive)
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(role => role.IsSystem)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(role => role.CreatedAt)
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(role => role.UpdatedAt)
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.HasIndex(role => role.NormalizedName)
            .HasDatabaseName("RoleNameIndex")
            .IsUnique();
    }
}
