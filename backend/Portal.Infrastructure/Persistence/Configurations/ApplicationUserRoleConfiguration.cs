using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portal.Infrastructure.Identity;

namespace Portal.Infrastructure.Persistence.Configurations;

internal sealed class ApplicationUserRoleConfiguration : IEntityTypeConfiguration<ApplicationUserRole>
{
    public void Configure(EntityTypeBuilder<ApplicationUserRole> builder)
    {
        builder.ToTable("AspNetUserRoles");

        builder.HasKey(userRole => new
        {
            userRole.UserId,
            userRole.RoleId,
        });

        builder.Property(userRole => userRole.AssignedAt)
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(userRole => userRole.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationRole>()
            .WithMany()
            .HasForeignKey(userRole => userRole.RoleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(userRole => userRole.AssignedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(userRole => userRole.RoleId);
    }
}
