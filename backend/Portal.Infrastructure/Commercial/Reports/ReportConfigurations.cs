using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portal.Domain.Commercial.Reports;

namespace Portal.Infrastructure.Commercial.Reports;

internal sealed class ReportConfiguration : IEntityTypeConfiguration<CommercialReport>
{
    public void Configure(EntityTypeBuilder<CommercialReport> b)
    {
        b.ToTable("CommercialReports"); b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(180).IsRequired();
        b.Property(x => x.Version).IsConcurrencyToken();
        b.Property(x => x.DataJson).HasColumnType("text").IsRequired();
        b.HasIndex(x => new { x.OwnerUserId, x.UpdatedAt });
    }
}
internal sealed class ReportSourceConfiguration : IEntityTypeConfiguration<CommercialReportSource>
{
    public void Configure(EntityTypeBuilder<CommercialReportSource> b)
    {
        b.ToTable("CommercialReportSources"); b.HasKey(x => x.Id);
        b.Property(x => x.FileName).HasMaxLength(255).IsRequired();
        b.Property(x => x.Kind).HasMaxLength(30).IsRequired();
        b.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
        b.HasIndex(x => new { x.ReportId, x.Sha256 });
        b.HasIndex(x => new { x.OwnerUserId, x.Kind, x.Sha256 }).IsUnique();
        b.HasIndex(x => new { x.Kind, x.Sha256 }).IsUnique().HasFilter("\"Kind\" = 'ops'");
        b.HasOne<CommercialReport>().WithMany().HasForeignKey(x => x.ReportId).OnDelete(DeleteBehavior.Restrict);
    }
}
internal sealed class OpReportRecordConfiguration : IEntityTypeConfiguration<ProductionOrderReportRecord>
{
    public void Configure(EntityTypeBuilder<ProductionOrderReportRecord> b)
    {
        b.ToTable("CommercialOpReportRecords"); b.HasKey(x => x.Id);
        b.Property(x => x.Number).HasMaxLength(100).IsRequired();
        b.Property(x => x.Code).HasMaxLength(100).IsRequired();
        b.Property(x => x.Client).HasMaxLength(500).IsRequired();
        b.Property(x => x.Product).HasMaxLength(500).IsRequired();
        b.Property(x => x.DataJson).HasColumnType("text").IsRequired().IsConcurrencyToken();
        b.HasIndex(x => new { x.ProductionOrderId, x.OrderVersion }).IsUnique();
        b.HasIndex(x => new { x.SourceId, x.SourceRow }).IsUnique();
        b.HasIndex(x => x.Number);
        b.HasOne<CommercialReportSource>().WithMany().HasForeignKey(x => x.SourceId).OnDelete(DeleteBehavior.Restrict);
    }
}
