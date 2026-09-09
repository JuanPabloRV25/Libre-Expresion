using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portal.Domain.Auditing;
using Portal.Infrastructure.Identity;

namespace Portal.Infrastructure.Persistence.Configurations;

internal sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.ToTable("AuditEvents");

        builder.HasKey(auditEvent => auditEvent.Id);

        builder.Property(auditEvent => auditEvent.Action)
            .HasColumnType("varchar(150)")
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(auditEvent => auditEvent.EntityType)
            .HasColumnType("varchar(100)")
            .HasMaxLength(100);

        builder.Property(auditEvent => auditEvent.EntityId)
            .HasColumnType("text");

        builder.Property(auditEvent => auditEvent.Result)
            .HasColumnType("varchar(30)")
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(auditEvent => auditEvent.OccurredAt)
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(auditEvent => auditEvent.IpAddress)
            .HasColumnType("inet");

        builder.Property(auditEvent => auditEvent.UserAgent)
            .HasColumnType("text");

        builder.Property(auditEvent => auditEvent.Metadata)
            .HasColumnType("jsonb");

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(auditEvent => auditEvent.ActorUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(auditEvent => auditEvent.ActorUserId);
        builder.HasIndex(auditEvent => auditEvent.OccurredAt);
        builder.HasIndex(auditEvent => auditEvent.Action);
    }
}
