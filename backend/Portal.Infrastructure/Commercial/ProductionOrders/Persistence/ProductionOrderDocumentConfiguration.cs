using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portal.Domain.Commercial.ProductionOrders;
using Portal.Infrastructure.Identity;

namespace Portal.Infrastructure.Commercial.ProductionOrders.Persistence;

internal sealed class ProductionOrderDocumentConfiguration : IEntityTypeConfiguration<ProductionOrderDocument>
{
    public void Configure(EntityTypeBuilder<ProductionOrderDocument> builder)
    {
        builder.ToTable("CommercialProductionOrderDocuments");
        builder.HasKey(document => document.Id);
        builder.Property(document => document.Type).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(document => document.Applicability).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(document => document.OriginalFileName).HasMaxLength(255).IsRequired();
        builder.Property(document => document.StorageKey).HasMaxLength(180).IsRequired();
        builder.Property(document => document.ContentType).HasMaxLength(150).IsRequired();
        builder.Property(document => document.Sha256).HasMaxLength(64).IsRequired();
        builder.Property(document => document.UploadedAt).HasColumnType("timestamptz").IsRequired();
        builder.Property(document => document.SupersededAt).HasColumnType("timestamptz");
        builder.HasOne<ProductionOrder>().WithMany(order => order.Documents).HasForeignKey(document => document.ProductionOrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(document => document.UploadedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(document => new { document.ProductionOrderId, document.Type, document.IsActive });
        builder.HasIndex(document => document.StorageKey).IsUnique();
    }
}
