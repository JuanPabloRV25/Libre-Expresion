namespace Portal.Application.Commercial.Reports;

public sealed class OpRecordData
{
    // A..U of the OP register. Unavailable data stays empty, never guessed.
    public string[] Cells { get; set; } = new string[21];
    public string? PortalCode { get; set; }
    public Guid? RegistryId { get; set; }
    public int RegistryVersion { get; set; } = 1;
    public Guid? PreviousRecordId { get; set; }
    public Guid? SupersededById { get; set; }
    public string? Origin { get; set; }
    public int[] CommercialOverrides { get; set; } = [];
}

public sealed record OpRecordDto(Guid Id, string Number, string Code, string Client,
    string Product, int? OrderVersion, DateTimeOffset CapturedAt, string Origin, OpRecordData Data,
    int RegistryVersion = 1, Guid? PreviousRecordId = null);

public sealed class SalesDetail
{
    public string Id { get; set; } = "";
    public Guid SourceId { get; set; }
    public string SourceFile { get; set; } = "";
    public string Sheet { get; set; } = "";
    public int SourceRow { get; set; }
    public string Number { get; set; } = "";
    public string Date { get; set; } = "";
    public string Client { get; set; } = "";
    public string Term { get; set; } = "";
    public decimal RawAmount { get; set; }
    public string Detail { get; set; } = "";
    public string Line { get; set; } = "";
    public string Seller { get; set; } = "";
    public string ManagerOp { get; set; } = "";
    public string DocumentId { get; set; } = "";
    public string MatchKey { get; set; } = "";
    public Guid? SelectedOpId { get; set; }
    public string SelectedOpNumber { get; set; } = "";
    public string SelectedProduct { get; set; } = "";
    public bool UsePortalCode { get; set; }
    public string? ManualGroup { get; set; }
    public bool Reviewed { get; set; }
    public bool Excluded { get; set; }
    public string Reason { get; set; } = "";
}

public sealed class SalesDocument
{
    public string Id { get; set; } = "";
    public string Number { get; set; } = "";
    public string Client { get; set; } = "";
    public decimal? SourceAmount { get; set; }
    public decimal? ConfirmedAmount { get; set; }
    public string Reason { get; set; } = "";
}

public sealed class SalesGroupEdit
{
    public string Key { get; set; } = "";
    public string Factura { get; set; } = "";
    public decimal? Amount { get; set; }
    public string? Client { get; set; }
    public string? Line { get; set; }
    public string? Seller { get; set; }
    public string Reason { get; set; } = "";
}

public sealed class SalesReportData
{
    public PreparedReportData? Preparation { get; set; }
    public Guid CurrentSourceId { get; set; }
    public string SourceFile { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public List<SalesDetail> Details { get; set; } = [];
    public List<SalesDocument> Documents { get; set; } = [];
    public List<SalesGroupEdit> Edits { get; set; } = [];
    public List<SalesGroupEdit> UnappliedEdits { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public List<ReportSourceWarningEvidence> SourceWarningEvidence { get; set; } = [];
}

// Source evidence describes the unchanged workbook; it is not an extra sale or
// a decision to include/exclude a row in the prepared result.
public sealed class ReportSourceWarningEvidence
{
    public string Warning { get; set; } = "";
    public ReportSourceRowEvidence[] Rows { get; set; } = [];
}

public sealed class ReportSourceRowEvidence
{
    public string SourceFile { get; set; } = "";
    public string Sheet { get; set; } = "";
    public int SourceRow { get; set; }
    public ReportSourceCellEvidence[] Cells { get; set; } = [];
}

public sealed record ReportSourceCellEvidence(string Column, string Header, string Value);

public sealed record SalesGroup(string Key, string DocumentId, string Number, string Date,
    string Op, string Product, string Client, string Term, decimal? Amount, string Factura,
    string Line, string Seller, string[] Details, string[] DetailIds, string[] Issues,
    bool Modified)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? AmountMode { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? OpCount { get; init; }
}
public sealed record DocumentControl(string Id, string Number, string Client,
    decimal? Expected, decimal Distributed, decimal? Difference);
public sealed record ReportDto(Guid Id, string Name, int Version, DateTimeOffset UpdatedAt,
    int? LastExportedVersion, SalesReportData Data, SalesGroup[] Groups,
    DocumentControl[] Controls, bool CanExport, bool CanExportDraft = false,
    bool CanApprove = false, bool CanExportFinal = false);
public sealed record ReportSummary(Guid Id, string Name, int Version, DateTimeOffset UpdatedAt,
    int? LastExportedVersion);
public sealed record SaveReportRequest(int Version, string Name, List<DetailDecision> Details,
    List<SalesGroupEdit> Edits, List<DocumentDecision> Documents);
public sealed record DetailDecision(string Id, Guid? SelectedOpId, bool UsePortalCode,
    bool Reviewed, bool Excluded, string Reason, string? ManualGroup, string? ProductIfMissing = null);
public sealed record DocumentDecision(string Id, decimal? ConfirmedAmount, string Reason);
public sealed record ExportReportRequest(int Version);
public sealed record ApproveReportRequest(int Version);
public sealed record SavePreparedReportRequest(int Version, string? Name, List<PreparedRowEdit> RowEdits,
    List<ReportReviewCommand>? Decisions = null);
public sealed record SaveOpRecordRequest(OpRecordData Data, string? ExpectedCapturedAt = null);
public sealed record SourceReplacementPreview(string FileName, bool Identical, int Retained,
    int Added, int Removed, ReportDto Preview);
public sealed class ReportValidationException(string message, int status = 400) : Exception(message)
{
    public int Status { get; } = status;
}
