namespace Portal.Application.Commercial.ProductionOrders;

public sealed record ProductionOrderListQuery(string? Search, string? Status, bool AssignedToMe = false);

public sealed record ProductionOrderReviewerDto(Guid Id, string Name, string? SecondaryLabel);

public sealed record ProductionOrderReviewersResult(
    ProductionOrderOperationStatus Status,
    IReadOnlyList<ProductionOrderReviewerDto>? Reviewers = null,
    string? ErrorMessage = null);

public sealed record ProductionOrderPersonDto(Guid Id, string Name);

public sealed record ProductionOrderSummaryDto(
    Guid Id,
    string Code,
    string Status,
    int Version,
    string? CustomerOrderNumber,
    string? QuotationNumber,
    string? ClientName,
    string? ProductName,
    DateOnly? DeliveryDate,
    decimal? Quantity,
    decimal? TotalValue,
    ProductionOrderPersonDto CommercialOwner,
    ProductionOrderPersonDto? ProductionOwner,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<string> AllowedActions);

public sealed record CommercialOrderDto(
    string? CustomerOrderNumber,
    string? QuotationNumber,
    DateOnly? DeliveryDate,
    string? ClientName,
    string? ProductName,
    string? ReferenceNumber,
    string? ClientPurchaseOrder,
    decimal? Quantity,
    decimal? UnitValue,
    decimal? TotalValue,
    string? CityCountry,
    string? Address,
    string WorkType,
    bool PrintColorProof,
    string DieType,
    string? OpenSize,
    string? ClosedSize,
    string? Observations,
    string? AdditionalSpecifications,
    string? ReceptionContact,
    string? DeliveryAddress,
    string? ReceptionSchedule,
    bool PartialDelivery,
    decimal? PartialDeliveryQuantity,
    string? LegalContractRequirements,
    DateOnly? DispatchDay,
    string QualityCertificateMode,
    string TechnicalSheetMode);

public sealed record ProductionOrderDataDto(
    DateOnly? PlanningDate,
    string? PlanningManager,
    DateOnly? MaterialCutDate,
    string? CuttingManager,
    DateTimeOffset? PrintStartShift1,
    string? PrintingManagerShift1,
    DateTimeOffset? PrintStartShift2,
    string? PrintingManagerShift2,
    DateTimeOffset? FinishingStart,
    string? FinishingManager,
    DateTimeOffset? DieCutStart,
    string? DieCutManager,
    string? DieMachine,
    string? DieNumber,
    decimal? DieTotalProcessed,
    decimal? DieConforming,
    decimal? DieNonConforming,
    DateTimeOffset? GluingStart,
    string? GluingManager,
    string? GlueType,
    decimal? GlueTotalProcessed,
    decimal? GlueConforming,
    decimal? GlueNonConforming,
    DateTimeOffset? QualityReviewDate,
    string? QualityReviewer,
    bool? QualityApproved,
    string? QualityNotes);

public sealed record ProductionOrderMaterialDto(
    Guid Id,
    int Position,
    string? Material,
    string? Weight,
    string? Caliber,
    string? OptionalSpecifications,
    string? SheetSize,
    decimal? SheetQuantity,
    string? CutSize,
    decimal? FractionPerSheet,
    decimal? FitPerFraction,
    decimal? TotalCutQuantity,
    decimal? ConformingQuantity,
    decimal? NonConformingQuantity);

public sealed record ProductionOrderPrintLineDto(
    Guid Id,
    int Position,
    string? Product,
    string? Inks,
    string? Process,
    string? Specials,
    string? Machine,
    string? Mounting,
    decimal? ShotsToProcess,
    decimal? ConformingQuantity,
    decimal? NonConformingQuantity);

public sealed record ProductionOrderFinishDto(
    Guid Id,
    int Position,
    string? Specification,
    bool Front,
    bool Back,
    bool Reserve,
    decimal? TotalProcessed,
    decimal? ConformingQuantity,
    decimal? NonConformingQuantity);

public sealed record ProductionOrderHistoryDto(
    Guid Id,
    string Status,
    ProductionOrderPersonDto Actor,
    string? Note,
    DateTimeOffset OccurredAt);

public sealed record ProductionOrderDocumentDto(
    Guid Id,
    string Type,
    string Applicability,
    string OriginalFileName,
    string ContentType,
    long Size,
    string Sha256,
    DateTimeOffset UploadedAt);

public sealed record ProductionOrderChecklistDto(
    bool QuotationReady,
    string PurchaseOrder,
    string Design,
    bool Complete);

public sealed record RelatedProductionOrderDto(
    Guid Id,
    string Code,
    string Status,
    string? ProductName,
    decimal? Quantity);

public sealed record ProductionOrderDetailDto(
    Guid Id,
    string Code,
    string Status,
    int Version,
    Guid? SourceOrderId,
    Guid OperationGroupId,
    ProductionOrderPersonDto CommercialOwner,
    ProductionOrderPersonDto? CurrentAssignee,
    ProductionOrderPersonDto? ReviewOwner,
    ProductionOrderPersonDto? ProductionOwner,
    CommercialOrderDto Commercial,
    ProductionOrderDataDto Production,
    IReadOnlyList<ProductionOrderMaterialDto> Materials,
    IReadOnlyList<ProductionOrderPrintLineDto> PrintLines,
    IReadOnlyList<ProductionOrderFinishDto> Finishes,
    IReadOnlyList<ProductionOrderHistoryDto> History,
    IReadOnlyList<ProductionOrderDocumentDto> Documents,
    ProductionOrderChecklistDto Checklist,
    IReadOnlyList<RelatedProductionOrderDto> RelatedOrders,
    string? LastReturnReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? ReviewSubmittedAt,
    DateTimeOffset? ReviewReturnedAt,
    DateTimeOffset? ProductionReceivedAt,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<string> AllowedActions);

public sealed record CommercialMaterialInput(
    string? Material,
    string? Weight,
    string? Caliber,
    string? OptionalSpecifications);

public sealed record CommercialPrintLineInput(
    string? Product,
    string? Inks,
    string? Process,
    string? Specials);

public sealed record CommercialFinishInput(
    string? Specification,
    bool Front,
    bool Back,
    bool Reserve);

public sealed record SaveCommercialOrderCommand(
    int? Version,
    string? CustomerOrderNumber,
    string? QuotationNumber,
    DateOnly? DeliveryDate,
    string? ClientName,
    string? ProductName,
    string? ReferenceNumber,
    string? ClientPurchaseOrder,
    decimal? Quantity,
    decimal? UnitValue,
    string? CityCountry,
    string? Address,
    string? WorkType,
    bool PrintColorProof,
    string? DieType,
    string? OpenSize,
    string? ClosedSize,
    string? Observations,
    string? AdditionalSpecifications,
    string? ReceptionContact,
    string? DeliveryAddress,
    string? ReceptionSchedule,
    bool PartialDelivery,
    decimal? PartialDeliveryQuantity,
    string? LegalContractRequirements,
    DateOnly? DispatchDay,
    string? QualityCertificateMode,
    string? TechnicalSheetMode,
    IReadOnlyList<CommercialMaterialInput>? Materials,
    IReadOnlyList<CommercialPrintLineInput>? PrintLines,
    IReadOnlyList<CommercialFinishInput>? Finishes);

public sealed record ProductionMaterialInput(
    Guid Id,
    string? SheetSize,
    decimal? SheetQuantity,
    string? CutSize,
    decimal? FractionPerSheet,
    decimal? FitPerFraction,
    decimal? TotalCutQuantity,
    decimal? ConformingQuantity,
    decimal? NonConformingQuantity);

public sealed record ProductionPrintLineInput(
    Guid Id,
    string? Machine,
    string? Mounting,
    decimal? ShotsToProcess,
    decimal? ConformingQuantity,
    decimal? NonConformingQuantity);

public sealed record ProductionFinishInput(
    Guid Id,
    decimal? TotalProcessed,
    decimal? ConformingQuantity,
    decimal? NonConformingQuantity);

public sealed record SaveProductionOrderCommand(
    int Version,
    DateOnly? PlanningDate,
    string? PlanningManager,
    DateOnly? MaterialCutDate,
    string? CuttingManager,
    DateTimeOffset? PrintStartShift1,
    string? PrintingManagerShift1,
    DateTimeOffset? PrintStartShift2,
    string? PrintingManagerShift2,
    DateTimeOffset? FinishingStart,
    string? FinishingManager,
    DateTimeOffset? DieCutStart,
    string? DieCutManager,
    string? DieMachine,
    string? DieNumber,
    decimal? DieTotalProcessed,
    decimal? DieConforming,
    decimal? DieNonConforming,
    DateTimeOffset? GluingStart,
    string? GluingManager,
    string? GlueType,
    decimal? GlueTotalProcessed,
    decimal? GlueConforming,
    decimal? GlueNonConforming,
    DateTimeOffset? QualityReviewDate,
    string? QualityReviewer,
    bool? QualityApproved,
    string? QualityNotes,
    IReadOnlyList<ProductionMaterialInput>? Materials,
    IReadOnlyList<ProductionPrintLineInput>? PrintLines,
    IReadOnlyList<ProductionFinishInput>? Finishes);

public enum ProductionOrderOperationStatus
{
    Success,
    NotFound,
    Forbidden,
    Invalid,
    Conflict,
}

public sealed record ProductionOrderOperationResult(
    ProductionOrderOperationStatus Status,
    ProductionOrderDetailDto? Order = null,
    string? ErrorCode = null,
    string? ErrorMessage = null,
    IReadOnlyDictionary<string, string[]>? ValidationErrors = null);

public sealed record UploadedFileCommand(string FileName, string ContentType, long Length, Stream Content);

public sealed record QuotationPriceOptionDto(decimal Quantity, decimal UnitValue, decimal NetValue);

public sealed record QuotationItemDto(
    int Index,
    string Description,
    string? ProductName,
    string? OpenSize,
    string? Material,
    string? Caliber,
    string? Inks,
    string? Process,
    IReadOnlyList<QuotationPriceOptionDto> Options);

public sealed record QuotationPreviewDto(
    string FileName,
    string Format,
    string TemplateFingerprint,
    string? QuotationNumber,
    string? ClientName,
    string? CityCountry,
    string? Address,
    IReadOnlyList<QuotationItemDto> Items,
    IReadOnlyList<string> Warnings);

public sealed record QuotationPreviewResult(
    bool Success,
    QuotationPreviewDto? Preview = null,
    string? ErrorCode = null,
    string? ErrorMessage = null);

public sealed record ImportQuotationSelection(int ItemIndex, int OptionIndex);

public sealed record DocumentDownloadResult(
    bool Success,
    Stream? Content = null,
    string? ContentType = null,
    string? FileName = null,
    string? ErrorCode = null,
    string? ErrorMessage = null);
