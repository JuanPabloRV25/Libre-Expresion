namespace Portal.Application.Commercial.Reports;

public interface ICommercialReportService
{
    Task<ReportSummary[]> List(CancellationToken ct);
    Task<ReportDto> Get(Guid id, CancellationToken ct);
    Task<OpRecordDto[]> Ops(string? search, CancellationToken ct);
    Task<object> ImportOps(byte[] bytes, string fileName, CancellationToken ct);
    Task<OpRecordDto> SaveOp(Guid? id, SaveOpRecordRequest request, CancellationToken ct);
    Task<ReportDto> Create(byte[] bytes, string fileName, CancellationToken ct);
    Task<ReportDto> Save(Guid id, SaveReportRequest request, CancellationToken ct);
    Task<ReportDto> SavePrepared(Guid id, SavePreparedReportRequest request, bool preview, CancellationToken ct);
    Task<ReportDto> Approve(Guid id, int version, CancellationToken ct);
    Task<SourceReplacementPreview> Replace(Guid id, int version, byte[] bytes, string fileName, bool confirm, CancellationToken ct);
    Task<byte[]> Export(Guid id, int version, CancellationToken ct);
    Task<byte[]> ExportDraft(Guid id, int version, CancellationToken ct);
    Task<byte[]> ExportOps(CancellationToken ct);
    Task<(byte[] Bytes, string Name)> Original(Guid sourceId, CancellationToken ct);
}
