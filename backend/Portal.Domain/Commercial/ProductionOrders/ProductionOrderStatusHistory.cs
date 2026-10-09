namespace Portal.Domain.Commercial.ProductionOrders;

public sealed class ProductionOrderStatusHistory
{
    public Guid Id { get; set; }

    public Guid ProductionOrderId { get; set; }

    public ProductionOrderStatus Status { get; set; }

    public Guid ActorUserId { get; set; }

    public string? Note { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public ProductionOrder ProductionOrder { get; set; } = null!;
}
