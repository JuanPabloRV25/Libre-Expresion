namespace Portal.Application.Commercial.ProductionOrders;

public interface IProductionOrderDocumentStorage
{
    Task<StoredDocument> SaveAsync(Guid orderId, UploadedFileCommand file, CancellationToken cancellationToken = default);
    Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default);
    Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default);
}

public sealed record StoredDocument(
    string StorageKey,
    string OriginalFileName,
    string ContentType,
    long Size,
    string Sha256);
