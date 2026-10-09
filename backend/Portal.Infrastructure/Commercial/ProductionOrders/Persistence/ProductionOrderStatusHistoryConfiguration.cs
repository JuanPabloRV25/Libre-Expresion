using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portal.Domain.Commercial.ProductionOrders;
using Portal.Infrastructure.Identity;

namespace Portal.Infrastructure.Commercial.ProductionOrders.Persistence;

internal sealed class ProductionOrderStatusHistoryConfiguration : IEntityTypeConfiguration<ProductionOrderStatusHistory>
{
    public void Configure(EntityTypeBuilder<ProductionOrderStatusHistory> builder)
    {
        builder.ToTable("CommercialProductionOrderStatusHistory");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Status).HasConversion<string>().HasColumnType("varchar(40)").HasMaxLength(40).IsRequired();
        builder.Property(item => item.Note).HasColumnType("varchar(500)").HasMaxLength(500);
        builder.Property(item => item.OccurredAt).HasColumnType("timestamptz").IsRequired();
        builder.HasOne(item => item.ProductionOrder).WithMany(order => order.StatusHistory).HasForeignKey(item => item.ProductionOrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(item => item.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(item => new { item.ProductionOrderId, item.OccurredAt });
    }
}
