namespace Portal.Domain.Commercial.ProductionOrders;

public sealed class ProductionOrderImport
{
    public Guid Id { get; set; }
    public Guid ProductionOrderId { get; set; }
    public Guid SourceDocumentId { get; set; }
    public string TemplateFingerprint { get; set; } = string.Empty;
    public string ImporterVersion { get; set; } = string.Empty;
    public string ExtractedDataJson { get; set; } = "{}";
    public string WarningsJson { get; set; } = "[]";
    public Guid ImportedByUserId { get; set; }
    public DateTimeOffset ImportedAt { get; set; }
}
