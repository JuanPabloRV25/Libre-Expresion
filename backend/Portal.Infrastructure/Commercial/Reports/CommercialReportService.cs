using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Portal.Application.Commercial.Reports;
using Portal.Application.Identity;
using Portal.Domain.Auditing;
using Portal.Domain.Commercial.ProductionOrders;
using Portal.Domain.Commercial.Reports;
using Portal.Infrastructure.Persistence;

namespace Portal.Infrastructure.Commercial.Reports;

public sealed partial class CommercialReportService(ApplicationDbContext db, ICurrentUserService users, TimeProvider clock) : ICommercialReportService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private async Task<CurrentUserInfo> User(string permission, CancellationToken ct)
    {
        var user = await users.GetCurrentAsync(ct);
        if (user is null || !user.IsActive || user.MustChangePassword || !user.Permissions.Contains(permission))
            throw new ReportValidationException("No tienes permiso para realizar esta acción.", 403);
        return user;
    }
    private static bool Global(CurrentUserInfo user) => user.Permissions.Contains(CommercialPermissionCodes.OrdersManage);
    private async Task<CommercialReport> Report(Guid id, CurrentUserInfo user, CancellationToken ct)
    {
        var report = await db.Set<CommercialReport>().SingleOrDefaultAsync(r => r.Id == id, ct);
        if (report is null || report.OwnerUserId != user.Id && !Global(user)) throw new ReportValidationException("No se encontró el reporte.", 404);
        return report;
    }
    private static SalesReportData Data(CommercialReport report) =>
        ReportInvoicePolicy.ApplyDefaults(JsonSerializer.Deserialize<SalesReportData>(report.DataJson, Json)!);
    private static ReportDto Dto(CommercialReport report, SalesReportData? data = null, CurrentUserInfo? user = null)
    {
        data ??= Data(report);
        ReportInvoicePolicy.ApplyDefaults(data);
        var canEdit = user?.Permissions.Contains(ReportPermissionCodes.Edit) == true;
        var canDownload = user?.Permissions.Contains(ReportPermissionCodes.Export) == true;
        if (data.Preparation is { } prepared)
        {
            var summary = ReportReviewPolicy.Summary(data);
            if (prepared.Review is not null) prepared.Review.Summary = summary;
            var canApprove = canEdit && prepared.RuleVersion >= 3 && prepared.Review is not null && prepared.Rows.Count > 0 && summary.PendingCases == 0;
            var canFinal = canDownload && prepared.RuleVersion >= 3 && prepared.Review is not null && prepared.Rows.Count > 0
                && summary.PendingCases == 0 && ReportReviewPolicy.IsApproved(prepared, report.Version);
            return new(report.Id, report.Name, report.Version, report.UpdatedAt, report.LastExportedVersion, data,
                prepared.Rows.ToArray(), [], canFinal, canDownload && prepared.Rows.Count > 0, canApprove, canFinal);
        }
        var groups = SalesReportEngine.Groups(data); var controls = SalesReportEngine.Controls(data, groups);
        // Earlier snapshots remain editable and downloadable as marked drafts. They
        // cannot acquire a final approval merely because their legacy checks pass.
        return new(report.Id, report.Name, report.Version, report.UpdatedAt, report.LastExportedVersion, data, groups, controls,
            false, canDownload && groups.Length > 0, false, false);
    }
    private void Audit(CurrentUserInfo user, Guid id, string action, object? detail = null)
    {
        db.AuditEvents.Add(new AuditEvent { Id = Guid.NewGuid(), ActorUserId = user.Id, Action = action,
            EntityType = "CommercialReport", EntityId = id.ToString(), Result = "success", Metadata = JsonSerializer.Serialize(detail ?? new { }, Json), OccurredAt = clock.GetUtcNow() });
    }
    private async Task Commit(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new ReportValidationException("El reporte cambió en otra ventana. Recárgalo antes de guardar; tus cambios siguen en pantalla.", 409); }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        { throw new ReportValidationException("Esta fuente ya se guardó en otra solicitud. Recarga la lista; no se duplicaron registros.", 409); }
    }
    private static void Version(CommercialReport report, int version)
    {
        if (report.Version != version) throw new ReportValidationException("El reporte cambió en otra ventana. Recárgalo antes de continuar.", 409);
    }
    private static string FileName(string name) => Path.GetFileName(name.Replace('\\', '/')) is { Length: > 0 and <= 255 } safe ? safe : "reporte.xlsx";
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private CommercialReportSource Source(byte[] bytes, string fileName, string kind, Guid owner, Guid? reportId = null) =>
        new() { Id = Guid.NewGuid(), OwnerUserId = owner, ReportId = reportId, OriginalBytes = bytes, Sha256 = Hash(bytes),
            FileName = FileName(fileName), Kind = kind, CreatedAt = clock.GetUtcNow() };

    public async Task<ReportSummary[]> List(CancellationToken ct)
    {
        var user = await User(ReportPermissionCodes.View, ct);
        var query = db.Set<CommercialReport>().AsNoTracking();
        if (!Global(user)) query = query.Where(r => r.OwnerUserId == user.Id);
        return await query.OrderByDescending(r => r.UpdatedAt).Select(r => new ReportSummary(r.Id, r.Name, r.Version, r.UpdatedAt, r.LastExportedVersion)).ToArrayAsync(ct);
    }
    public async Task<ReportDto> Get(Guid id, CancellationToken ct)
    {
        var user = await User(ReportPermissionCodes.View, ct);
        var report = await Report(id, user, ct);
        var data = Data(report);
        var fileFindings = data.Preparation?.Review?.Cases.Where(c => c.Scope == "file")
            .SelectMany(c => c.Findings).Where(f => f.Code == "source_warning" && f.SourceRows.Length == 0).ToArray() ?? [];
        if (fileFindings.Length > 0 && data.SourceWarningEvidence.Count == 0)
        {
            var source = await db.Set<CommercialReportSource>().AsNoTracking().SingleOrDefaultAsync(s =>
                s.Id == data.CurrentSourceId && s.OwnerUserId == report.OwnerUserId && s.Kind == "manager" && s.Sha256 == data.Sha256, ct);
            if (source is not null && Hash(source.OriginalBytes) == source.Sha256)
            {
                var evidence = ReportExcel.ReadOmittedSalesRows(source.OriginalBytes, source.FileName);
                if (evidence.Rows.Length > 0 && data.Warnings.Contains(evidence.Warning))
                {
                    // Metadata is returned only in this read response. The saved
                    // revision, its decisions and its original bytes are untouched.
                    data.SourceWarningEvidence.Add(evidence);
                    foreach (var finding in fileFindings.Where(f => f.Reason == evidence.Warning))
                        finding.SourceRows = evidence.Rows;
                }
            }
        }
        return Dto(report, data, user);
    }

    private static OpRecordDto OpDto(ProductionOrderReportRecord r)
    {
        var data = RegisterData(r);
        return new(r.Id, r.Number, r.Code, r.Client, r.Product, r.OrderVersion, r.CapturedAt,
            data.Origin ?? (r.ProductionOrderId.HasValue ? "Portal: datos enviados a Producción" : "Registro histórico"),
            data, data.RegistryVersion, data.PreviousRecordId);
    }
    public async Task<OpRecordDto[]> Ops(string? search, CancellationToken ct)
    {
        await User(ReportPermissionCodes.View, ct);
        var latest = await ActiveOps(ct);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = SalesReportEngine.Normalize(search);
            latest = latest.Where(r => SalesReportEngine.Normalize(r.Number + r.Code + r.Client + r.Product).Contains(term)).ToArray();
        }
        return latest.Select(OpDto).ToArray();
    }
    public Task<object> ImportOps(byte[] bytes, string fileName, CancellationToken ct) => ImportPermanentOps(bytes, fileName, ct);
    private async Task ProposeMatches(SalesReportData data, CancellationToken ct)
    {
        var records = await db.Set<ProductionOrderReportRecord>().AsNoTracking().ToArrayAsync(ct);
        var latest = records.Where(r => !r.ProductionOrderId.HasValue).Concat(records.Where(r => r.ProductionOrderId.HasValue)
            .GroupBy(r => r.ProductionOrderId).Select(g => g.OrderByDescending(r => r.OrderVersion).First())).ToArray();
        foreach (var detail in data.Details.Where(d => !d.SelectedOpId.HasValue && d.ManagerOp.Length > 0))
        {
            var candidates = latest.Where(r => SalesReportEngine.Normalize(r.Number) == SalesReportEngine.Normalize(detail.ManagerOp)
                || r.Code.Length > 0 && SalesReportEngine.Normalize(r.Code) == SalesReportEngine.Normalize(detail.ManagerOp)).ToArray();
            if (candidates.Length != 1) continue;
            var match = candidates[0];
            if (SalesReportEngine.Normalize(match.Client) != SalesReportEngine.Normalize(detail.Client) ||
                SalesReportEngine.Normalize(match.Product).Length == 0 || !SalesReportEngine.Normalize(detail.Detail).Contains(SalesReportEngine.Normalize(match.Product))) continue;
            detail.SelectedOpId = match.Id; detail.SelectedOpNumber = match.Number; detail.SelectedProduct = match.Product;
            // Only an unchanged number, unique OP, exact client and product evidence
            // can be linked automatically. Every correction remains for review.
            detail.Reviewed = SalesReportEngine.Normalize(detail.ManagerOp) == SalesReportEngine.Normalize(match.Number);
        }
    }
    public Task<ReportDto> Create(byte[] bytes, string fileName, CancellationToken ct) => PrepareReport(bytes, fileName, ct);
    public async Task<ReportDto> Save(Guid id, SaveReportRequest request, CancellationToken ct)
    {
        var user = await User(ReportPermissionCodes.Edit, ct); var report = await Report(id, user, ct); Version(report, request.Version);
        var data = Data(report);
        if (data.Preparation is not null) throw new ReportValidationException("Este reporte utiliza la edición del resultado preparado.", 409);
        if (request.Name is null || request.Details is null || request.Edits is null || request.Documents is null ||
            request.Name.Trim().Length is 0 or > 180 || request.Details.Count != data.Details.Count ||
            request.Details.Select(d => d.Id).Distinct().Count() != data.Details.Count ||
            request.Details.Any(d => !data.Details.Any(x => x.Id == d.Id))) throw new ReportValidationException("El reporte no coincide con sus datos originales. Recárgalo.");
        var ids = request.Details.Where(d => d.SelectedOpId.HasValue).Select(d => d.SelectedOpId!.Value).Distinct().ToArray();
        var ops = await db.Set<ProductionOrderReportRecord>().AsNoTracking().Where(r => ids.Contains(r.Id)).ToDictionaryAsync(r => r.Id, ct);
        foreach (var decision in request.Details)
        {
            var detail = data.Details.Single(d => d.Id == decision.Id);
            if (decision.Reason is null || decision.Reason.Length > 1000 || decision.ManualGroup?.Length > 100) throw new ReportValidationException("El motivo o nombre del grupo supera el tamaño permitido.");
            if (decision.Excluded && string.IsNullOrWhiteSpace(decision.Reason)) throw new ReportValidationException("Indica por qué excluyes el detalle de la fila " + detail.SourceRow + ".");
            if (decision.SelectedOpId is { } opId)
            {
                if (!ops.TryGetValue(opId, out var op)) throw new ReportValidationException("La OP seleccionada ya no está disponible.");
                if (decision.UsePortalCode && op.Code.Length == 0) throw new ReportValidationException("Este registro histórico no tiene código del Portal.");
                detail.SelectedOpId = opId; detail.SelectedOpNumber = decision.UsePortalCode ? op.Code : op.Number; detail.SelectedProduct = op.Product;
                if (string.IsNullOrWhiteSpace(op.Product))
                {
                    if (decision.ProductIfMissing?.Length > 500) throw new ReportValidationException("El producto supera 500 caracteres.");
                    detail.SelectedProduct = decision.ProductIfMissing?.Trim() ?? "";
                }
                detail.UsePortalCode = decision.UsePortalCode;
            }
            else { detail.SelectedOpId = null; detail.SelectedOpNumber = ""; detail.SelectedProduct = ""; detail.UsePortalCode = false; }
            detail.Reviewed = decision.Reviewed && detail.SelectedOpId.HasValue;
            detail.Excluded = decision.Excluded; detail.Reason = decision.Reason.Trim();
            detail.ManualGroup = string.IsNullOrWhiteSpace(decision.ManualGroup) ? null : decision.ManualGroup.Trim();
        }
        var keys = data.Details.Where(d => !d.Excluded).Select(SalesReportEngine.GroupKey).ToHashSet();
        if (request.Edits.Select(e => e.Key).Distinct().Count() != request.Edits.Count)
            throw new ReportValidationException("Hay ajustes duplicados para un mismo grupo.");
        foreach (var edit in request.Edits)
        {
            CheckMoney(edit.Amount);
            if (edit.Factura is null || edit.Reason is null || edit.Factura.Length > 50 || edit.Client?.Length > 500 || edit.Line?.Length > 200 || edit.Seller?.Length > 200 || edit.Reason.Length > 1000)
                throw new ReportValidationException("Uno de los campos editados supera el tamaño permitido.");
            edit.Factura = edit.Factura.Trim();
        }
        if (request.Documents.Select(d => d.Id).Distinct().Count() != request.Documents.Count || request.Documents.Count != data.Documents.Count)
            throw new ReportValidationException("Los controles no coinciden con el reporte original.");
        foreach (var decision in request.Documents)
        {
            var doc = data.Documents.SingleOrDefault(d => d.Id == decision.Id) ?? throw new ReportValidationException("Control desconocido.");
            CheckMoney(decision.ConfirmedAmount);
            if (decision.Reason is null || decision.Reason.Length > 1000) throw new ReportValidationException("El motivo supera 1000 caracteres.");
            if (decision.ConfirmedAmount != null && decision.ConfirmedAmount != doc.SourceAmount && string.IsNullOrWhiteSpace(decision.Reason))
                throw new ReportValidationException("Explica el total confirmado distinto del valor de Manager para NUMERO " + doc.Number + ".");
            doc.ConfirmedAmount = decision.ConfirmedAmount; doc.Reason = decision.Reason.Trim();
        }
        var detached = request.Edits.Where(e => !keys.Contains(e.Key)).ToArray();
        if (detached.Length > 0)
        {
            data.UnappliedEdits.AddRange(detached);
            data.Warnings.Add("Cambió la agrupación. Los ajustes anteriores están conservados en Ajustes por reconfirmar.");
        }
        report.Name = request.Name.Trim(); data.Edits = request.Edits.Where(e => keys.Contains(e.Key)).ToList();
        report.DataJson = JsonSerializer.Serialize(data, Json); report.Version++; report.UpdatedAt = clock.GetUtcNow();
        Audit(user, report.Id, "commercial.report.saved", new { report.Version, details = request.Details, edits = request.Edits, documents = request.Documents });
        await Commit(ct); return Dto(report, data, user);
    }
    private static void CheckMoney(decimal? amount)
    {
        if (amount.HasValue && (amount.Value is < -999999999999m or > 999999999999m || decimal.Round(amount.Value, 2) != amount.Value))
            throw new ReportValidationException("El importe debe tener como máximo dos decimales y estar dentro del tamaño permitido.");
    }
    public async Task<SourceReplacementPreview> Replace(Guid id, int version, byte[] bytes, string fileName, bool confirm, CancellationToken ct)
    {
        var user = await User(ReportPermissionCodes.Edit, ct); var report = await Report(id, user, ct); Version(report, version);
        var old = Data(report);
        if (old.Preparation is not null) throw new ReportValidationException("Prepara un nuevo reporte para otro archivo; el resultado actual permanece guardado.", 409);
        var source = Source(bytes, fileName, "manager", report.OwnerUserId, report.Id);
        if (source.Sha256 == old.Sha256) return new(source.FileName, true, old.Details.Count, 0, 0, Dto(report, user: user));
        var previous = await db.Set<CommercialReportSource>().AsNoTracking().SingleOrDefaultAsync(s => s.OwnerUserId == report.OwnerUserId && s.Kind == "manager" && s.Sha256 == source.Sha256, ct);
        if (previous is not null && previous.ReportId != id) throw new ReportValidationException("Ese archivo ya pertenece a otro reporte. Ábrelo desde la lista.", 409);
        source = previous ?? source;
        var incoming = ReportExcel.ReadSales(bytes, source.Id, source.FileName, source.Sha256);
        SalesReportEngine.ReplaceSource(old, incoming, out var retained, out var added, out var removed);
        await ProposeMatches(incoming, ct);
        if (confirm)
        {
            if (previous is null) db.Add(source);
            report.DataJson = JsonSerializer.Serialize(incoming, Json); report.Version++; report.UpdatedAt = clock.GetUtcNow();
            Audit(user, report.Id, "commercial.report.source_replaced", new { source.Sha256, retained, added, removed }); await Commit(ct);
        }
        return new(source.FileName, false, retained, added, removed, Dto(report, incoming, user));
    }
    public async Task<ReportDto> Approve(Guid id, int version, CancellationToken ct)
    {
        var user = await User(ReportPermissionCodes.Edit, ct);
        var report = await Report(id, user, ct); Version(report, version);
        var data = Data(report);
        if (data.Preparation?.Review is null)
            throw new ReportValidationException("Este reporte conserva una versión anterior. Puedes descargar un BORRADOR; prepara un nuevo reporte para revisarlo y aprobarlo.", 422);
        if (!Dto(report, data, user).CanApprove)
            throw new ReportValidationException("Resuelve todos los casos pendientes antes de aprobar VENTAS MES.", 422);
        var nextVersion = report.Version + 1;
        ReportReviewPolicy.Approve(data.Preparation, user.Id.ToString(), nextVersion, clock.GetUtcNow());
        report.Version = nextVersion;
        report.UpdatedAt = clock.GetUtcNow();
        report.DataJson = JsonSerializer.Serialize(data, Json);
        Audit(user, report.Id, "commercial.report.approved", new { report.Version,
            approval = data.Preparation.Review.Approvals.Last() });
        await Commit(ct);
        return Dto(report, data, user);
    }
    public async Task<byte[]> Export(Guid id, int version, CancellationToken ct)
    {
        var user = await User(ReportPermissionCodes.Export, ct); var report = await Report(id, user, ct); Version(report, version);
        var dto = Dto(report, user: user);
        if (!dto.CanExportFinal) throw new ReportValidationException("La descarga final requiere resolver los pendientes y aprobar la versión guardada. Puedes descargar un BORRADOR mientras revisas.", 422);
        var bytes = ReportExcel.ExportSales(dto.Groups, dto.Data.Preparation is { } preparation && ReportPreparationEngine.IncompleteTotals(preparation));
        report.LastExportedVersion = report.Version;
        // This metadata-only update deliberately keeps the approved content version.
        // The concurrency token still rejects a simultaneous content edit.
        Audit(user, report.Id, "commercial.report.exported_final", new { exportedVersion = report.LastExportedVersion });
        await Commit(ct); return bytes;
    }
    public async Task<byte[]> ExportDraft(Guid id, int version, CancellationToken ct)
    {
        var user = await User(ReportPermissionCodes.Export, ct); var report = await Report(id, user, ct); Version(report, version);
        var dto = Dto(report, user: user);
        if (!dto.CanExportDraft) throw new ReportValidationException("El reporte no contiene registros guardados para descargar.", 422);
        var incomplete = dto.Data.Preparation is { } preparation && ReportPreparationEngine.IncompleteTotals(preparation);
        var bytes = ReportExcel.ExportSales(dto.Groups, incomplete, draft: true,
            previousVersion: dto.Data.Preparation?.Review is null);
        Audit(user, report.Id, "commercial.report.exported_draft", new { report.Version });
        await Commit(ct); return bytes;
    }
    public async Task<byte[]> ExportOps(CancellationToken ct)
    { await User(ReportPermissionCodes.Export, ct); return ReportExcel.ExportOps(await Ops(null, ct)); }
    public async Task<(byte[] Bytes, string Name)> Original(Guid sourceId, CancellationToken ct)
    {
        var user = await User(ReportPermissionCodes.View, ct);
        var source = await db.Set<CommercialReportSource>().AsNoTracking().SingleOrDefaultAsync(s => s.Id == sourceId, ct);
        if (source is null || source.Kind == "manager" && source.OwnerUserId != user.Id && !Global(user))
            throw new ReportValidationException("No se encontró la fuente.", 404);
        return (source.OriginalBytes, source.FileName);
    }
}
