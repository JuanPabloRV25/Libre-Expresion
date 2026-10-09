using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portal.Domain.Commercial.ProductionOrders;
using Portal.Infrastructure.Identity;

namespace Portal.Infrastructure.Commercial.ProductionOrders.Persistence;

internal sealed class ProductionOrderImportConfiguration : IEntityTypeConfiguration<ProductionOrderImport>
{
    public void Configure(EntityTypeBuilder<ProductionOrderImport> builder)
    {
        builder.ToTable("CommercialProductionOrderImports");
        builder.HasKey(import => import.Id);
        builder.Property(import => import.TemplateFingerprint).HasMaxLength(128).IsRequired();
        builder.Property(import => import.ImporterVersion).HasMaxLength(40).IsRequired();
        builder.Property(import => import.ExtractedDataJson).HasColumnType("jsonb").IsRequired();
        builder.Property(import => import.WarningsJson).HasColumnType("jsonb").IsRequired();
        builder.Property(import => import.ImportedAt).HasColumnType("timestamptz").IsRequired();
        builder.HasOne<ProductionOrder>().WithMany(order => order.Imports).HasForeignKey(import => import.ProductionOrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ProductionOrderDocument>().WithMany().HasForeignKey(import => import.SourceDocumentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(import => import.ImportedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(import => import.ProductionOrderId);
    }
}
