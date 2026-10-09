namespace Portal.Domain.Commercial.ProductionOrders;

public sealed class ProductionOrderPrintLine
{
    public Guid Id { get; set; }

    public Guid ProductionOrderId { get; set; }

    public int Position { get; set; }

    public string? Product { get; set; }

    public string? Inks { get; set; }

    public string? Process { get; set; }

    public string? Specials { get; set; }

    public string? Machine { get; set; }

    public string? Mounting { get; set; }

    public decimal? ShotsToProcess { get; set; }

    public decimal? ConformingQuantity { get; set; }

    public decimal? NonConformingQuantity { get; set; }

    public ProductionOrder ProductionOrder { get; set; } = null!;
}
