namespace Portal.Domain.Commercial.Reports;

public sealed class CommercialReport
{
    public Guid Id { get; set; }
    public Guid OwnerUserId { get; set; }
    public string Name { get; set; } = "";
    public int Version { get; set; } = 1;
    public string DataJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int? LastExportedVersion { get; set; }
}

public sealed class CommercialReportSource
{
    public Guid Id { get; set; }
    public Guid OwnerUserId { get; set; }
    public Guid? ReportId { get; set; }
    public string Kind { get; set; } = "";
    public string FileName { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public byte[] OriginalBytes { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ProductionOrderReportRecord
{
    public Guid Id { get; set; }
    public Guid? ProductionOrderId { get; set; }
    public int? OrderVersion { get; set; }
    public Guid? SourceId { get; set; }
    public int? SourceRow { get; set; }
    public string Number { get; set; } = "";
    public string Code { get; set; } = "";
    public string Client { get; set; } = "";
    public string Product { get; set; } = "";
    public string DataJson { get; set; } = "{}";
    public Guid CapturedByUserId { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
}

public static class ReportPermissionCodes
{
    public const string View = "commercial.reports.view";
    public const string Edit = "commercial.reports.edit";
    public const string Export = "commercial.reports.export";
    public static readonly string[] All = [View, Edit, Export];
}
