namespace Portal.Domain.Commercial.ProductionOrders;

public static class CommercialPermissionCodes
{
    public const string OrdersView = "commercial.production_orders.view";
    public const string OrdersCreate = "commercial.production_orders.create";
    public const string OrdersEditCommercial = "commercial.production_orders.edit_commercial";
    public const string OrdersSubmitForReview = "commercial.production_orders.submit_for_review";
    public const string OrdersReview = "commercial.production_orders.review";
    public const string OrdersSubmit = "commercial.production_orders.submit";
    public const string OrdersDuplicate = "commercial.production_orders.duplicate";
    public const string OrdersViewProduction = "commercial.production_orders.view_production";
    public const string OrdersEditProduction = "commercial.production_orders.edit_production";
    public const string OrdersComplete = "commercial.production_orders.complete";
    public const string OrdersManage = "commercial.production_orders.manage";

    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(
    [
        OrdersView,
        OrdersCreate,
        OrdersEditCommercial,
        OrdersSubmitForReview,
        OrdersReview,
        OrdersSubmit,
        OrdersDuplicate,
        OrdersViewProduction,
        OrdersEditProduction,
        OrdersComplete,
        OrdersManage,
        Reports.ReportPermissionCodes.View,
        Reports.ReportPermissionCodes.Edit,
        Reports.ReportPermissionCodes.Export,
    ]);
}
