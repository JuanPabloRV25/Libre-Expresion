using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portal.Domain.Commercial.ProductionOrders;

namespace Portal.Infrastructure.Commercial.ProductionOrders.Persistence;

internal sealed class ProductionOrderMaterialConfiguration : IEntityTypeConfiguration<ProductionOrderMaterial>
{
    public void Configure(EntityTypeBuilder<ProductionOrderMaterial> builder)
    {
        builder.ToTable("CommercialProductionOrderMaterials");
        builder.HasKey(item => item.Id);
        Text(builder, item => item.Material, 180);
        Text(builder, item => item.Weight, 80);
        Text(builder, item => item.Caliber, 80);
        Text(builder, item => item.OptionalSpecifications, 500);
        Text(builder, item => item.SheetSize, 100);
        Text(builder, item => item.CutSize, 100);
        Number(builder, item => item.SheetQuantity);
        Number(builder, item => item.FractionPerSheet);
        Number(builder, item => item.FitPerFraction);
        Number(builder, item => item.TotalCutQuantity);
        Number(builder, item => item.ConformingQuantity);
        Number(builder, item => item.NonConformingQuantity);
        builder.HasOne(item => item.ProductionOrder).WithMany(order => order.Materials).HasForeignKey(item => item.ProductionOrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(item => new { item.ProductionOrderId, item.Position }).IsUnique();
    }

    private static void Text<T>(EntityTypeBuilder<ProductionOrderMaterial> builder, System.Linq.Expressions.Expression<Func<ProductionOrderMaterial, T>> expression, int length) =>
        builder.Property(expression).HasColumnType($"varchar({length})").HasMaxLength(length);

    private static void Number(EntityTypeBuilder<ProductionOrderMaterial> builder, System.Linq.Expressions.Expression<Func<ProductionOrderMaterial, decimal?>> expression) =>
        builder.Property(expression).HasColumnType("numeric(18,2)");
}
