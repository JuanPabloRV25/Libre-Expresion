namespace Portal.Domain.Commercial.ProductionOrders;

public sealed class ProductionOrderDocument
{
    public Guid Id { get; set; }
    public Guid ProductionOrderId { get; set; }
    public ProductionOrderDocumentType Type { get; set; }
    public DocumentApplicabilityStatus Applicability { get; set; } = DocumentApplicabilityStatus.Pending;
    public string OriginalFileName { get; set; } = string.Empty;
    public string StorageKey { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long Size { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public Guid UploadedByUserId { get; set; }
    public DateTimeOffset UploadedAt { get; set; }
    public DateTimeOffset? SupersededAt { get; set; }
}
