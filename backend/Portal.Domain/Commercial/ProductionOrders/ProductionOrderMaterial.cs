namespace Portal.Domain.Commercial.ProductionOrders;

public sealed class ProductionOrderMaterial
{
    public Guid Id { get; set; }

    public Guid ProductionOrderId { get; set; }

    public int Position { get; set; }

    public string? Material { get; set; }

    public string? Weight { get; set; }

    public string? Caliber { get; set; }

    public string? OptionalSpecifications { get; set; }

    public string? SheetSize { get; set; }

    public decimal? SheetQuantity { get; set; }

    public string? CutSize { get; set; }

    public decimal? FractionPerSheet { get; set; }

    public decimal? FitPerFraction { get; set; }

    public decimal? TotalCutQuantity { get; set; }

    public decimal? ConformingQuantity { get; set; }

    public decimal? NonConformingQuantity { get; set; }

    public ProductionOrder ProductionOrder { get; set; } = null!;
}
