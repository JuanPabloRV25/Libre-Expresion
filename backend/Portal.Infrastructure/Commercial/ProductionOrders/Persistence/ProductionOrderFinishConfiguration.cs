using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portal.Domain.Commercial.ProductionOrders;

namespace Portal.Infrastructure.Commercial.ProductionOrders.Persistence;

internal sealed class ProductionOrderFinishConfiguration : IEntityTypeConfiguration<ProductionOrderFinish>
{
    public void Configure(EntityTypeBuilder<ProductionOrderFinish> builder)
    {
        builder.ToTable("CommercialProductionOrderFinishes");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Specification).HasColumnType("varchar(300)").HasMaxLength(300);
        builder.Property(item => item.TotalProcessed).HasColumnType("numeric(18,2)");
        builder.Property(item => item.ConformingQuantity).HasColumnType("numeric(18,2)");
        builder.Property(item => item.NonConformingQuantity).HasColumnType("numeric(18,2)");
        builder.HasOne(item => item.ProductionOrder).WithMany(order => order.Finishes).HasForeignKey(item => item.ProductionOrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(item => new { item.ProductionOrderId, item.Position }).IsUnique();
    }
}
