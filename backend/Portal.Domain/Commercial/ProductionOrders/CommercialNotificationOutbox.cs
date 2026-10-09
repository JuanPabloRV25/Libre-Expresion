namespace Portal.Domain.Commercial.ProductionOrders;

public sealed class CommercialNotificationOutbox
{
    public Guid Id { get; set; }
    public Guid ProductionOrderId { get; set; }
    public string NotificationType { get; set; } = string.Empty;
    public Guid RecipientUserId { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public CommercialOutboxStatus Status { get; set; } = CommercialOutboxStatus.Pending;
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public string? LastError { get; set; }
}
