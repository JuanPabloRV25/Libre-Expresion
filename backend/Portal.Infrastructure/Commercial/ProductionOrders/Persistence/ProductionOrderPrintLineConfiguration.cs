using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portal.Domain.Commercial.ProductionOrders;

namespace Portal.Infrastructure.Commercial.ProductionOrders.Persistence;

internal sealed class ProductionOrderPrintLineConfiguration : IEntityTypeConfiguration<ProductionOrderPrintLine>
{
    public void Configure(EntityTypeBuilder<ProductionOrderPrintLine> builder)
    {
        builder.ToTable("CommercialProductionOrderPrintLines");
        builder.HasKey(item => item.Id);
        Text(builder, item => item.Product, 180);
        Text(builder, item => item.Inks, 300);
        Text(builder, item => item.Process, 300);
        Text(builder, item => item.Specials, 300);
        Text(builder, item => item.Machine, 180);
        Text(builder, item => item.Mounting, 180);
        Number(builder, item => item.ShotsToProcess);
        Number(builder, item => item.ConformingQuantity);
        Number(builder, item => item.NonConformingQuantity);
        builder.HasOne(item => item.ProductionOrder).WithMany(order => order.PrintLines).HasForeignKey(item => item.ProductionOrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(item => new { item.ProductionOrderId, item.Position }).IsUnique();
    }

    private static void Text<T>(EntityTypeBuilder<ProductionOrderPrintLine> builder, System.Linq.Expressions.Expression<Func<ProductionOrderPrintLine, T>> expression, int length) =>
        builder.Property(expression).HasColumnType($"varchar({length})").HasMaxLength(length);

    private static void Number(EntityTypeBuilder<ProductionOrderPrintLine> builder, System.Linq.Expressions.Expression<Func<ProductionOrderPrintLine, decimal?>> expression) =>
        builder.Property(expression).HasColumnType("numeric(18,2)");
}
