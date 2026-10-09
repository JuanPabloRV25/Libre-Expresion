namespace Portal.Application.Commercial.ProductionOrders;

public interface IQuotationImporter
{
    Task<QuotationPreviewResult> PreviewAsync(UploadedFileCommand file, CancellationToken cancellationToken = default);
}
