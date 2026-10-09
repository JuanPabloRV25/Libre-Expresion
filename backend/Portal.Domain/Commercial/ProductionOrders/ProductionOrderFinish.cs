namespace Portal.Domain.Commercial.ProductionOrders;

public sealed class ProductionOrderFinish
{
    public Guid Id { get; set; }

    public Guid ProductionOrderId { get; set; }

    public int Position { get; set; }

    public string? Specification { get; set; }

    public bool Front { get; set; }

    public bool Back { get; set; }

    public bool Reserve { get; set; }

    public decimal? TotalProcessed { get; set; }

    public decimal? ConformingQuantity { get; set; }

    public decimal? NonConformingQuantity { get; set; }

    public ProductionOrder ProductionOrder { get; set; } = null!;
}
