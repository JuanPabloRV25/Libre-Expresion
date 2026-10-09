namespace Portal.Domain.Commercial.ProductionOrders;

public sealed class ProductionOrder
{
    public Guid Id { get; set; }

    public long Consecutive { get; set; }

    public ProductionOrderStatus Status { get; set; } = ProductionOrderStatus.Draft;

    public int Version { get; set; } = 1;

    public Guid CommercialOwnerUserId { get; set; }

    public Guid CreatedByUserId { get; set; }

    public Guid LastUpdatedByUserId { get; set; }

    public Guid? ProductionOwnerUserId { get; set; }

    public Guid? CurrentAssigneeUserId { get; set; }

    public Guid? ReviewOwnerUserId { get; set; }

    public Guid? SourceOrderId { get; set; }

    public Guid OperationGroupId { get; set; }

    public string? CustomerOrderNumber { get; set; }

    public string? QuotationNumber { get; set; }

    public DocumentApplicabilityStatus PurchaseOrderApplicability { get; set; }

    public DocumentApplicabilityStatus DesignApplicability { get; set; }

    public DateOnly? DeliveryDate { get; set; }

    public string? ClientName { get; set; }

    public string? ProductName { get; set; }

    public string? ReferenceNumber { get; set; }

    public string? ClientPurchaseOrder { get; set; }

    public decimal? Quantity { get; set; }

    public decimal? UnitValue { get; set; }

    public string? CityCountry { get; set; }

    public string? Address { get; set; }

    public CommercialWorkType WorkType { get; set; }

    public bool PrintColorProof { get; set; }

    public DieType DieType { get; set; }

    public string? OpenSize { get; set; }

    public string? ClosedSize { get; set; }

    public string? Observations { get; set; }

    public string? AdditionalSpecifications { get; set; }

    public string? ReceptionContact { get; set; }

    public string? DeliveryAddress { get; set; }

    public string? ReceptionSchedule { get; set; }

    public bool PartialDelivery { get; set; }

    public decimal? PartialDeliveryQuantity { get; set; }

    public string? LegalContractRequirements { get; set; }

    public DateOnly? DispatchDay { get; set; }

    public DocumentDeliveryMode QualityCertificateMode { get; set; }

    public DocumentDeliveryMode TechnicalSheetMode { get; set; }

    public DateOnly? PlanningDate { get; set; }

    public string? PlanningManager { get; set; }

    public DateOnly? MaterialCutDate { get; set; }

    public string? CuttingManager { get; set; }

    public DateTimeOffset? PrintStartShift1 { get; set; }

    public string? PrintingManagerShift1 { get; set; }

    public DateTimeOffset? PrintStartShift2 { get; set; }

    public string? PrintingManagerShift2 { get; set; }

    public DateTimeOffset? FinishingStart { get; set; }

    public string? FinishingManager { get; set; }

    public DateTimeOffset? DieCutStart { get; set; }

    public string? DieCutManager { get; set; }

    public string? DieMachine { get; set; }

    public string? DieNumber { get; set; }

    public decimal? DieTotalProcessed { get; set; }

    public decimal? DieConforming { get; set; }

    public decimal? DieNonConforming { get; set; }

    public DateTimeOffset? GluingStart { get; set; }

    public string? GluingManager { get; set; }

    public string? GlueType { get; set; }

    public decimal? GlueTotalProcessed { get; set; }

    public decimal? GlueConforming { get; set; }

    public decimal? GlueNonConforming { get; set; }

    public DateTimeOffset? QualityReviewDate { get; set; }

    public string? QualityReviewer { get; set; }

    public bool? QualityApproved { get; set; }

    public string? QualityNotes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? SubmittedAt { get; set; }

    public DateTimeOffset? ReviewSubmittedAt { get; set; }

    public DateTimeOffset? ReviewReturnedAt { get; set; }

    public DateTimeOffset? ProductionReceivedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public ICollection<ProductionOrderMaterial> Materials { get; set; } = [];

    public ICollection<ProductionOrderPrintLine> PrintLines { get; set; } = [];

    public ICollection<ProductionOrderFinish> Finishes { get; set; } = [];

    public ICollection<ProductionOrderStatusHistory> StatusHistory { get; set; } = [];

    public ICollection<ProductionOrderDocument> Documents { get; set; } = [];

    public ICollection<ProductionOrderImport> Imports { get; set; } = [];
}
