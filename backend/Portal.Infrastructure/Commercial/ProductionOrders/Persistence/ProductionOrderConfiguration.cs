using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portal.Domain.Commercial.ProductionOrders;
using Portal.Infrastructure.Identity;

namespace Portal.Infrastructure.Commercial.ProductionOrders.Persistence;

internal sealed class ProductionOrderConfiguration : IEntityTypeConfiguration<ProductionOrder>
{
    public void Configure(EntityTypeBuilder<ProductionOrder> builder)
    {
        builder.ToTable("CommercialProductionOrders");
        builder.HasKey(order => order.Id);
        builder.Property(order => order.Consecutive).ValueGeneratedOnAdd();
        builder.HasIndex(order => order.Consecutive).IsUnique();
        builder.Property(order => order.Status).HasConversion<string>().HasColumnType("varchar(40)").HasMaxLength(40).IsRequired();
        builder.Property(order => order.Version).IsConcurrencyToken().IsRequired();

        Text(builder, order => order.CustomerOrderNumber, 100);
        Text(builder, order => order.QuotationNumber, 100);
        builder.Property(order => order.PurchaseOrderApplicability).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(order => order.DesignApplicability).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(order => order.DeliveryDate).HasColumnType("date");
        Text(builder, order => order.ClientName, 180);
        Text(builder, order => order.ProductName, 180);
        Text(builder, order => order.ReferenceNumber, 100);
        Text(builder, order => order.ClientPurchaseOrder, 100);
        Number(builder, order => order.Quantity);
        Number(builder, order => order.UnitValue);
        Text(builder, order => order.CityCountry, 180);
        Text(builder, order => order.Address, 500);
        builder.Property(order => order.WorkType).HasConversion<string>().HasColumnType("varchar(30)").HasMaxLength(30).IsRequired();
        builder.Property(order => order.DieType).HasConversion<string>().HasColumnType("varchar(30)").HasMaxLength(30).IsRequired();
        Text(builder, order => order.OpenSize, 100);
        Text(builder, order => order.ClosedSize, 100);
        Text(builder, order => order.Observations, 2_000);
        Text(builder, order => order.AdditionalSpecifications, 2_000);
        Text(builder, order => order.ReceptionContact, 180);
        Text(builder, order => order.DeliveryAddress, 500);
        Text(builder, order => order.ReceptionSchedule, 180);
        Number(builder, order => order.PartialDeliveryQuantity);
        Text(builder, order => order.LegalContractRequirements, 2_000);
        builder.Property(order => order.DispatchDay).HasColumnType("date");
        DeliveryMode(builder, order => order.QualityCertificateMode);
        DeliveryMode(builder, order => order.TechnicalSheetMode);

        builder.Property(order => order.PlanningDate).HasColumnType("date");
        Text(builder, order => order.PlanningManager, 180);
        builder.Property(order => order.MaterialCutDate).HasColumnType("date");
        Text(builder, order => order.CuttingManager, 180);
        Text(builder, order => order.PrintingManagerShift1, 180);
        Text(builder, order => order.PrintingManagerShift2, 180);
        Text(builder, order => order.FinishingManager, 180);
        Text(builder, order => order.DieCutManager, 180);
        Text(builder, order => order.DieMachine, 120);
        Text(builder, order => order.DieNumber, 120);
        Number(builder, order => order.DieTotalProcessed);
        Number(builder, order => order.DieConforming);
        Number(builder, order => order.DieNonConforming);
        Text(builder, order => order.GluingManager, 180);
        Text(builder, order => order.GlueType, 120);
        Number(builder, order => order.GlueTotalProcessed);
        Number(builder, order => order.GlueConforming);
        Number(builder, order => order.GlueNonConforming);
        Text(builder, order => order.QualityReviewer, 180);
        Text(builder, order => order.QualityNotes, 2_000);

        builder.Property(order => order.CreatedAt).HasColumnType("timestamptz").IsRequired();
        builder.Property(order => order.UpdatedAt).HasColumnType("timestamptz").IsRequired();
        builder.Property(order => order.SubmittedAt).HasColumnType("timestamptz");
        builder.Property(order => order.ReviewSubmittedAt).HasColumnType("timestamptz");
        builder.Property(order => order.ReviewReturnedAt).HasColumnType("timestamptz");
        builder.Property(order => order.ProductionReceivedAt).HasColumnType("timestamptz");
        builder.Property(order => order.CompletedAt).HasColumnType("timestamptz");
        builder.Property(order => order.PrintStartShift1).HasColumnType("timestamptz");
        builder.Property(order => order.PrintStartShift2).HasColumnType("timestamptz");
        builder.Property(order => order.FinishingStart).HasColumnType("timestamptz");
        builder.Property(order => order.DieCutStart).HasColumnType("timestamptz");
        builder.Property(order => order.GluingStart).HasColumnType("timestamptz");
        builder.Property(order => order.QualityReviewDate).HasColumnType("timestamptz");

        UserReference(builder, order => order.CommercialOwnerUserId, DeleteBehavior.Restrict);
        UserReference(builder, order => order.CreatedByUserId, DeleteBehavior.Restrict);
        UserReference(builder, order => order.LastUpdatedByUserId, DeleteBehavior.Restrict);
        UserReference(builder, order => order.ProductionOwnerUserId, DeleteBehavior.SetNull);
        UserReference(builder, order => order.CurrentAssigneeUserId, DeleteBehavior.SetNull);
        UserReference(builder, order => order.ReviewOwnerUserId, DeleteBehavior.SetNull);
        builder.HasOne<ProductionOrder>().WithMany().HasForeignKey(order => order.SourceOrderId).OnDelete(DeleteBehavior.SetNull);
        builder.Property(order => order.OperationGroupId).IsRequired();
        builder.HasIndex(order => order.OperationGroupId);
        builder.HasIndex(order => order.Status);
        builder.HasIndex(order => order.CommercialOwnerUserId);
        builder.HasIndex(order => order.ProductionOwnerUserId);
        builder.HasIndex(order => order.CurrentAssigneeUserId);
        builder.HasIndex(order => order.ReviewOwnerUserId);
        builder.HasIndex(order => order.CustomerOrderNumber);
        builder.HasIndex(order => order.ClientName);
        builder.HasIndex(order => order.CreatedAt);
    }

    private static void Text<T>(EntityTypeBuilder<ProductionOrder> builder, System.Linq.Expressions.Expression<Func<ProductionOrder, T>> expression, int length) =>
        builder.Property(expression).HasColumnType($"varchar({length})").HasMaxLength(length);

    private static void Number(EntityTypeBuilder<ProductionOrder> builder, System.Linq.Expressions.Expression<Func<ProductionOrder, decimal?>> expression) =>
        builder.Property(expression).HasColumnType("numeric(18,2)");

    private static void DeliveryMode(EntityTypeBuilder<ProductionOrder> builder, System.Linq.Expressions.Expression<Func<ProductionOrder, DocumentDeliveryMode>> expression) =>
        builder.Property(expression).HasConversion<string>().HasColumnType("varchar(20)").HasMaxLength(20).IsRequired();

    private static void UserReference(EntityTypeBuilder<ProductionOrder> builder, System.Linq.Expressions.Expression<Func<ProductionOrder, object?>> expression, DeleteBehavior deleteBehavior) =>
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(expression).OnDelete(deleteBehavior);
}
