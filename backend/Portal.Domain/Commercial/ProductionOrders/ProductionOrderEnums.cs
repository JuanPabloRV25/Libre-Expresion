namespace Portal.Domain.Commercial.ProductionOrders;

public enum ProductionOrderStatus
{
    Draft,
    PendingCommercialReview,
    CorrectionRequired,
    ReadyForProduction,
    InProduction,
    Completed,
    Cancelled,
}

public enum ProductionOrderDocumentType
{
    Quotation,
    PurchaseOrder,
    Design,
}

public enum DocumentApplicabilityStatus
{
    Pending,
    Attached,
    NotApplicable,
}

public enum CommercialOutboxStatus
{
    Pending,
    Sent,
    Failed,
}

public enum CommercialWorkType
{
    Unspecified,
    New,
    Change,
    Repeat,
    Replacement,
}

public enum DieType
{
    None,
    Existing,
    New,
}

public enum DocumentDeliveryMode
{
    None,
    Physical,
    Digital,
    Both,
}
