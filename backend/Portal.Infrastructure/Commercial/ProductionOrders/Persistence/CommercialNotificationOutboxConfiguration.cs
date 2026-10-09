using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portal.Domain.Commercial.ProductionOrders;
using Portal.Infrastructure.Identity;

namespace Portal.Infrastructure.Commercial.ProductionOrders.Persistence;

internal sealed class CommercialNotificationOutboxConfiguration : IEntityTypeConfiguration<CommercialNotificationOutbox>
{
    public void Configure(EntityTypeBuilder<CommercialNotificationOutbox> builder)
    {
        builder.ToTable("CommercialNotificationOutbox");
        builder.HasKey(message => message.Id);
        builder.Property(message => message.NotificationType).HasMaxLength(80).IsRequired();
        builder.Property(message => message.PayloadJson).HasColumnType("jsonb").IsRequired();
        builder.Property(message => message.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(message => message.NextAttemptAt).HasColumnType("timestamptz").IsRequired();
        builder.Property(message => message.CreatedAt).HasColumnType("timestamptz").IsRequired();
        builder.Property(message => message.SentAt).HasColumnType("timestamptz");
        builder.Property(message => message.LastError).HasMaxLength(1000);
        builder.HasOne<ProductionOrder>().WithMany().HasForeignKey(message => message.ProductionOrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(message => message.RecipientUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(message => new { message.Status, message.NextAttemptAt });
    }
}
