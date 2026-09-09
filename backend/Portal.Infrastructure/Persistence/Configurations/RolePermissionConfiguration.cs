using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portal.Domain.Permissions;
using Portal.Infrastructure.Identity;

namespace Portal.Infrastructure.Persistence.Configurations;

internal sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("RolePermissions");

        builder.HasKey(rolePermission => new
        {
            rolePermission.RoleId,
            rolePermission.PermissionId,
        });

        builder.Property(rolePermission => rolePermission.AssignedAt)
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.HasOne<ApplicationRole>()
            .WithMany()
            .HasForeignKey(rolePermission => rolePermission.RoleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Permission>()
            .WithMany()
            .HasForeignKey(rolePermission => rolePermission.PermissionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(rolePermission => rolePermission.AssignedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(rolePermission => rolePermission.PermissionId);
    }
}
