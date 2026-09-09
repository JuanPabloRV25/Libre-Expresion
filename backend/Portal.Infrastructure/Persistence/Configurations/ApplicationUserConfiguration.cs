using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portal.Domain.Areas;
using Portal.Infrastructure.Identity;

namespace Portal.Infrastructure.Persistence.Configurations;

internal sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.ToTable("AspNetUsers");

        builder.Property(user => user.UserName)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(user => user.NormalizedUserName)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(user => user.Email)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(user => user.NormalizedEmail)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(user => user.FirstName)
            .HasColumnType("varchar(120)")
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(user => user.LastName)
            .HasColumnType("varchar(120)")
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(user => user.AdvisorCode)
            .HasColumnType("varchar(100)")
            .HasMaxLength(100);

        builder.Property(user => user.IsActive)
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(user => user.MustChangePassword)
            .IsRequired();

        builder.Property(user => user.CreatedAt)
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(user => user.UpdatedAt)
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.HasOne<Area>()
            .WithMany()
            .HasForeignKey(user => user.AreaId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(user => user.NormalizedUserName)
            .HasDatabaseName("UserNameIndex")
            .IsUnique();

        builder.HasIndex(user => user.NormalizedEmail)
            .HasDatabaseName("EmailIndex")
            .IsUnique();

        builder.HasIndex(user => user.AreaId);
        builder.HasIndex(user => user.IsActive);
    }
}
