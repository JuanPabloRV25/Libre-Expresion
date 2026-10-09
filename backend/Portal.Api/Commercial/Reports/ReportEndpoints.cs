using Portal.Api.Authentication;
using Portal.Application.Commercial.Reports;
using Portal.Domain.Commercial.Reports;
using Portal.Infrastructure.Commercial.Reports;

namespace Portal.Api.Commercial.Reports;

public static class ReportEndpoints
{
    private const string ExcelType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public static IEndpointRouteBuilder MapCommercialReportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/commercial/reports");
        group.AddEndpointFilter(async (context, next) =>
        {
            try { return await next(context); }
            catch (ReportValidationException ex) { return Results.Json(new { code = "commercial_report", message = ex.Message }, statusCode: ex.Status); }
        });
        group.MapGet("/", (ICommercialReportService service, CancellationToken ct) => service.List(ct)).RequireAuthorization(ReportPermissionCodes.View);
        group.MapGet("/ops", (string? search, ICommercialReportService service, CancellationToken ct) => service.Ops(search, ct)).RequireAuthorization(ReportPermissionCodes.View);
        group.MapPost("/ops", (SaveOpRecordRequest request, ICommercialReportService service, CancellationToken ct) => service.SaveOp(null, request, ct))
            .RequireAuthorization(ReportPermissionCodes.Edit).AddEndpointFilter<AntiforgeryEndpointFilter>();
        group.MapPut("/ops/{id:guid}", (Guid id, SaveOpRecordRequest request, ICommercialReportService service, CancellationToken ct) => service.SaveOp(id, request, ct))
            .RequireAuthorization(ReportPermissionCodes.Edit).AddEndpointFilter<AntiforgeryEndpointFilter>();
        group.MapGet("/ops/export", async (ICommercialReportService service, CancellationToken ct) => Results.File(await service.ExportOps(ct), ExcelType, "INFORME DE OPS.xlsx")).RequireAuthorization(ReportPermissionCodes.Export);
        group.MapPost("/ops/import", async (HttpRequest request, ICommercialReportService service, CancellationToken ct) =>
        { var file = await ReadFile(request, ct); return Results.Ok(await service.ImportOps(file.Bytes, file.Name, ct)); })
            .RequireAuthorization(ReportPermissionCodes.Edit).AddEndpointFilter<AntiforgeryEndpointFilter>();
        group.MapPost("/", async (HttpRequest request, ICommercialReportService service, CancellationToken ct) =>
        { var file = await ReadFile(request, ct); return Results.Ok(await service.Create(file.Bytes, file.Name, ct)); })
            .RequireAuthorization(ReportPermissionCodes.Edit).AddEndpointFilter<AntiforgeryEndpointFilter>();
        group.MapGet("/{id:guid}", (Guid id, ICommercialReportService service, CancellationToken ct) => service.Get(id, ct)).RequireAuthorization(ReportPermissionCodes.View);
        group.MapPost("/{id:guid}/prepared/preview", (Guid id, SavePreparedReportRequest request, ICommercialReportService service, CancellationToken ct) => service.SavePrepared(id, request, true, ct))
            .RequireAuthorization(ReportPermissionCodes.Edit).AddEndpointFilter<AntiforgeryEndpointFilter>();
        group.MapPut("/{id:guid}/prepared", (Guid id, SavePreparedReportRequest request, ICommercialReportService service, CancellationToken ct) => service.SavePrepared(id, request, false, ct))
            .RequireAuthorization(ReportPermissionCodes.Edit).AddEndpointFilter<AntiforgeryEndpointFilter>();
        group.MapPost("/{id:guid}/approve", (Guid id, ApproveReportRequest request, ICommercialReportService service, CancellationToken ct) => service.Approve(id, request.Version, ct))
            .RequireAuthorization(ReportPermissionCodes.Edit).AddEndpointFilter<AntiforgeryEndpointFilter>();
        group.MapPut("/{id:guid}", (Guid id, SaveReportRequest request, ICommercialReportService service, CancellationToken ct) => service.Save(id, request, ct))
            .RequireAuthorization(ReportPermissionCodes.Edit).AddEndpointFilter<AntiforgeryEndpointFilter>();
        group.MapPost("/{id:guid}/source", async (Guid id, HttpRequest request, ICommercialReportService service, CancellationToken ct) =>
        {
            var form = await request.ReadFormAsync(ct); var file = await ReadFile(request, ct);
            if (!int.TryParse(form["version"], out var version)) throw new ReportValidationException("Recarga el reporte antes de actualizar la fuente.");
            return Results.Ok(await service.Replace(id, version, file.Bytes, file.Name, form["confirm"] == "true", ct));
        }).RequireAuthorization(ReportPermissionCodes.Edit).AddEndpointFilter<AntiforgeryEndpointFilter>();
        group.MapPost("/{id:guid}/export", async (Guid id, ExportReportRequest request, ICommercialReportService service, CancellationToken ct) =>
            Results.File(await service.Export(id, request.Version, ct), ExcelType, "VENTAS MES.xlsx"))
            .RequireAuthorization(ReportPermissionCodes.Export).AddEndpointFilter<AntiforgeryEndpointFilter>();
        group.MapPost("/{id:guid}/export/draft", async (Guid id, ExportReportRequest request, ICommercialReportService service, CancellationToken ct) =>
            Results.File(await service.ExportDraft(id, request.Version, ct), ExcelType, "BORRADOR - VENTAS MES.xlsx"))
            .RequireAuthorization(ReportPermissionCodes.Export).AddEndpointFilter<AntiforgeryEndpointFilter>();
        group.MapGet("/sources/{sourceId:guid}", async (Guid sourceId, ICommercialReportService service, CancellationToken ct) =>
        { var source = await service.Original(sourceId, ct); return Results.File(source.Bytes, ExcelType, source.Name); }).RequireAuthorization(ReportPermissionCodes.View);
        return endpoints;
    }
    private static async Task<(byte[] Bytes, string Name)> ReadFile(HttpRequest request, CancellationToken ct)
    {
        if (!request.HasFormContentType) throw new ReportValidationException("Selecciona un Excel .xlsx.");
        var file = (await request.ReadFormAsync(ct)).Files.GetFile("file");
        if (file is null || file.Length is <= 0 or > ReportExcel.MaximumBytes || !file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new ReportValidationException("Selecciona un Excel .xlsx de hasta 10 MB.");
        using var stream = new MemoryStream(); await file.CopyToAsync(stream, ct); return (stream.ToArray(), file.FileName);
    }
}
