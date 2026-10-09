namespace Portal.Application.Commercial.ProductionOrders;

public interface IProductionOrderService
{
    Task<IReadOnlyList<ProductionOrderSummaryDto>> ListAsync(ProductionOrderListQuery query, CancellationToken cancellationToken = default);

    Task<ProductionOrderOperationResult> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<ProductionOrderOperationResult> CreateAsync(SaveCommercialOrderCommand command, CancellationToken cancellationToken = default);

    Task<ProductionOrderOperationResult> UpdateCommercialAsync(Guid id, SaveCommercialOrderCommand command, CancellationToken cancellationToken = default);

    Task<QuotationPreviewResult> PreviewQuotationAsync(UploadedFileCommand file, CancellationToken cancellationToken = default);

    Task<ProductionOrderOperationResult> ImportQuotationAsync(
        UploadedFileCommand file,
        ImportQuotationSelection selection,
        SaveCommercialOrderCommand? commercial,
        Guid? relatedOrderId,
        CancellationToken cancellationToken = default);

    Task<ProductionOrderOperationResult> ReplaceQuotationAsync(Guid id, int version, UploadedFileCommand file, ImportQuotationSelection selection, CancellationToken cancellationToken = default);

    Task<ProductionOrderOperationResult> AddDocumentAsync(Guid id, int version, string type, UploadedFileCommand file, CancellationToken cancellationToken = default);

    Task<ProductionOrderOperationResult> SetDocumentApplicabilityAsync(Guid id, int version, string type, bool notApplicable, CancellationToken cancellationToken = default);

    Task<DocumentDownloadResult> DownloadDocumentAsync(Guid id, Guid documentId, CancellationToken cancellationToken = default);

    Task<ProductionOrderOperationResult> DeleteDocumentAsync(Guid id, Guid documentId, int version, CancellationToken cancellationToken = default);

    Task<ProductionOrderOperationResult> DiscardDraftAsync(Guid id, int version, CancellationToken cancellationToken = default);

    Task<ProductionOrderReviewersResult> GetReviewersAsync(Guid id, CancellationToken cancellationToken = default);

    Task<ProductionOrderOperationResult> SubmitForReviewAsync(Guid id, int version, Guid? reviewerUserId, CancellationToken cancellationToken = default);

    Task<ProductionOrderOperationResult> ReturnForCorrectionAsync(Guid id, int version, string? reason, CancellationToken cancellationToken = default);

    Task<ProductionOrderOperationResult> SubmitAsync(Guid id, int version, string? customerOrderNumber, CancellationToken cancellationToken = default);

    Task<ProductionOrderOperationResult> DuplicateAsync(Guid id, CancellationToken cancellationToken = default);

    Task<ProductionOrderOperationResult> ReceiveAsync(Guid id, int version, CancellationToken cancellationToken = default);

    Task<ProductionOrderOperationResult> UpdateProductionAsync(Guid id, SaveProductionOrderCommand command, CancellationToken cancellationToken = default);

    Task<ProductionOrderOperationResult> CompleteAsync(Guid id, int version, CancellationToken cancellationToken = default);

    Task<ProductionOrderOperationResult> CancelAsync(Guid id, int version, string? reason, CancellationToken cancellationToken = default);
}
