using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Portal.Application.Commercial.Reports;
using Portal.Domain.Commercial.Reports;

namespace Portal.Infrastructure.Commercial.Reports;

public sealed partial class CommercialReportService
{
    private static readonly SemaphoreSlim PreparationGate = new(1, 1);
    private static OpRecordData RegisterData(ProductionOrderReportRecord record) =>
        JsonSerializer.Deserialize<OpRecordData>(record.DataJson)!;

    private async Task<ProductionOrderReportRecord[]> ActiveOps(CancellationToken ct)
    {
        var all = await db.Set<ProductionOrderReportRecord>().AsNoTracking().ToArrayAsync(ct);
        return all.Select(r => (Record: r, Data: RegisterData(r)))
            .Where(x => !x.Data.SupersededById.HasValue)
            .GroupBy(x => x.Data.RegistryId ?? x.Record.ProductionOrderId ?? x.Record.Id)
            .Select(g => g.OrderByDescending(x => x.Record.CapturedAt)
                .ThenByDescending(x => x.Data.RegistryVersion).ThenByDescending(x => x.Record.OrderVersion).First().Record)
            .OrderBy(r => r.SourceId).ThenBy(r => r.SourceRow).ThenBy(r => r.CapturedAt).ThenBy(r => r.Id).ToArray();
    }

    private async Task<ReportDto> PrepareReport(byte[] bytes, string fileName, CancellationToken ct)
    {
        var user = await User(ReportPermissionCodes.Edit, ct);
        await PreparationGate.WaitAsync(ct);
        try
        {
            // PostgreSQL lock also covers requests received by another API process.
            await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
            if (db.Database.IsNpgsql())
                await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(746204004)", ct);
            var hash = Hash(bytes);
            var source = await db.Set<CommercialReportSource>().SingleOrDefaultAsync(s =>
                s.OwnerUserId == user.Id && s.Kind == "manager" && s.Sha256 == hash, ct);
            var isNewSource = source is null;
            source ??= Source(bytes, fileName, "manager", user.Id);
            var data = ReportExcel.ReadSales(bytes, source.Id, source.FileName, source.Sha256, preparationMode: true);
            var history = (await ActiveOps(ct)).Select(OpDto).ToList();
            var prepared = ReportPreparationEngine.Prepare(data, history);
            ReportReviewPolicy.Reconcile(data);
            prepared.IdentityKey = SalesReportEngine.Key($"prepared-v{ReportPreparationEngine.CurrentRuleVersion}", "normalization-v1", "review-v1",
                source.Sha256, prepared.HistoryFingerprint);
            var existing = await db.Set<CommercialReport>().AsNoTracking().Where(r => r.OwnerUserId == user.Id).ToArrayAsync(ct);
            var same = existing.FirstOrDefault(r => Data(r).Preparation?.IdentityKey == prepared.IdentityKey);
            if (same is not null) return Dto(same, user: user);
            var name = "Ventas — " + source.FileName;
            var report = new CommercialReport
            {
                Id = Guid.NewGuid(), OwnerUserId = user.Id, Name = name[..Math.Min(name.Length, 180)],
                CreatedAt = clock.GetUtcNow(), UpdatedAt = clock.GetUtcNow(), DataJson = JsonSerializer.Serialize(data, Json)
            };
            foreach (var change in prepared.Changes)
            { change.ReportVersion = report.Version; change.OccurredAt = report.CreatedAt; }
            report.DataJson = JsonSerializer.Serialize(data, Json);
            if (isNewSource) { source.ReportId = report.Id; db.Add(source); }
            db.Add(report);
            Audit(user, report.Id, "commercial.report.prepared", new
            {
                source.Sha256, prepared.RuleVersion, prepared.IdentityKey,
                details = data.Details.Count, rows = prepared.Rows.Count, prepared.Status
            });
            await Commit(ct);
            if (transaction is not null) await transaction.CommitAsync(ct);
            return Dto(report, data, user);
        }
        finally { PreparationGate.Release(); }
    }

    public async Task<ReportDto> SavePrepared(Guid id, SavePreparedReportRequest request, bool preview, CancellationToken ct)
    {
        var user = await User(ReportPermissionCodes.Edit, ct);
        var report = await Report(id, user, ct); Version(report, request.Version);
        var data = Data(report);
        if (data.Preparation is null) throw new ReportValidationException("Este reporte conserva la edición de su versión anterior.", 409);
        var previous = Data(report).Preparation;
        if (request.RowEdits is null || request.RowEdits.Any(e => e is null) || request.RowEdits.Count > data.Preparation.Rows.Count ||
            request.RowEdits.Select(e => e.Key).Distinct().Count() != request.RowEdits.Count)
            throw new ReportValidationException("Los ajustes no coinciden con las filas del reporte.");
        if (request.Decisions is { } decisions && (decisions.Any(d => d is null) ||
            decisions.Count > (data.Preparation.Review?.Cases.Count ?? 0)))
            throw new ReportValidationException("Las decisiones no coinciden con los casos de este borrador.");
        if (request.Name is not null && request.Name.Trim().Length is 0 or > 180)
            throw new ReportValidationException("El nombre debe tener entre 1 y 180 caracteres.");
        foreach (var edit in request.RowEdits)
        {
            CheckMoney(edit.SetAmount ? edit.Amount : null);
            if (edit.Date is { Length: > 0 } && !DateOnly.TryParseExact(edit.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                throw new ReportValidationException("La fecha editada debe ser una fecha válida.");
            if (new[] { edit.Op, edit.Factura, edit.Number, edit.Client, edit.Term, edit.Detail, edit.Line, edit.Seller }
                .Any(value => value?.Length > 32767)) throw new ReportValidationException("El texto supera el espacio disponible en una celda de Excel.");
        }
        // Amount validation uses the final OP decision of this transaction, including
        // an OP selection and its manual total submitted together.
        var deferredAmounts = data.Preparation.RuleVersion >= 4
            ? request.RowEdits.Where(e => e.SetAmount && !e.Restore).Select(e => new PreparedRowEdit { Key = e.Key, SetAmount = true, Amount = e.Amount }).ToArray()
            : [];
        var firstEdits = deferredAmounts.Length == 0 ? request.RowEdits : request.RowEdits.Select(e =>
        {
            var copy = JsonSerializer.Deserialize<PreparedRowEdit>(JsonSerializer.Serialize(e))!;
            if (!copy.Restore) copy.SetAmount = false;
            return copy;
        }).ToList();
        ReportPreparationEngine.Apply(data, firstEdits, user.Id.ToString());
        // A patch remains an edit. Only an explicit, validated command records a
        // human OP decision, including confirmation of an unchanged N/A value.
        ReportReviewPolicy.Reconcile(data, previous, user.Id.ToString(), report.Version + 1, clock.GetUtcNow());
        if (data.Preparation.Review is not null || request.Decisions is { Count: > 0 })
            ReportReviewPolicy.ApplyDecisions(data, request.Decisions ?? [], user.Id.ToString(), report.Version + 1, clock.GetUtcNow());
        if (deferredAmounts.Length > 0)
        {
            var beforeAmounts = JsonSerializer.Deserialize<PreparedReportData>(JsonSerializer.Serialize(data.Preparation))!;
            ReportPreparationEngine.Apply(data, deferredAmounts, user.Id.ToString());
            ReportReviewPolicy.Reconcile(data, beforeAmounts, user.Id.ToString(), report.Version + 1, clock.GetUtcNow());
        }
        foreach (var change in data.Preparation.Changes.Where(c => c.Manual && c.ReportVersion == 0))
        { change.ReportVersion = report.Version + 1; change.OccurredAt = clock.GetUtcNow(); }
        if (preview) return Dto(report, data, user);
        var nameChanged = request.Name is not null && report.Name != request.Name.Trim();
        var contentChanged = ReportReviewPolicy.ContentFingerprint(previous!) != ReportReviewPolicy.ContentFingerprint(data.Preparation);
        var reviewChanged = previous!.Changes.Count != data.Preparation.Changes.Count ||
            (previous.Review?.Decisions.Count ?? 0) != (data.Preparation.Review?.Decisions.Count ?? 0);
        if (!nameChanged && !contentChanged && !reviewChanged) return Dto(report, user: user);
        // Every newly saved revision must be approved as that exact version.
        // Preserve the original approval record rather than moving its actor or
        // version to a renamed or edited revision without a new approval action.
        if (data.Preparation.Review is { } review)
            foreach (var approval in review.Approvals) approval.Valid = false;
        if (request.Name is not null) report.Name = request.Name.Trim();
        report.DataJson = JsonSerializer.Serialize(data, Json); report.Version++; report.UpdatedAt = clock.GetUtcNow();
        Audit(user, report.Id, "commercial.report.prepared_edited", new { report.Version, edits = request.RowEdits });
        if (request.Decisions is { Count: > 0 })
            Audit(user, report.Id, "commercial.report.decided", new { report.Version, decisions = request.Decisions });
        if (previous?.Review?.Approvals.Any(a => a.Valid) == true && data.Preparation.Review?.Approvals.All(a => !a.Valid) == true)
            Audit(user, report.Id, "commercial.report.approval_invalidated", new { report.Version });
        await Commit(ct); return Dto(report, data, user);
    }

    private static string FullRegisterKey(OpRecordData data) => SalesReportEngine.Key(data.Cells.Select(ReportPreparationEngine.Identity).ToArray());
    private static string RegisterIdentity(OpRecordData data) => SalesReportEngine.Key(
        new[] { 0, 1, 2, 3, 5, 6 }.Select(i => ReportPreparationEngine.Identity(data.Cells[i])).ToArray());

    private async Task<object> ImportPermanentOps(byte[] bytes, string fileName, CancellationToken ct)
    {
        var user = await User(ReportPermissionCodes.Edit, ct);
        await PreparationGate.WaitAsync(ct);
        try
        {
            await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
            if (db.Database.IsNpgsql()) await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(746204004)", ct);
            var source = Source(bytes, fileName, "ops", user.Id);
            if (await db.Set<CommercialReportSource>().AsNoTracking().AnyAsync(s => s.Kind == "ops" && s.Sha256 == source.Sha256, ct))
                return new { imported = 0, duplicate = true, message = "Este informe ya está incorporado. No se duplicaron OP." };
            var rows = ReportExcel.ReadOps(bytes, allowIncomplete: true);
            var active = (await ActiveOps(ct)).Select(r => (Record: r, Data: RegisterData(r))).ToArray();
            var exact = active.GroupBy(x => FullRegisterKey(x.Data)).ToDictionary(g => g.Key, g => g.Count());
            var identities = active.GroupBy(x => RegisterIdentity(x.Data)).ToDictionary(g => g.Key, g => g.Count());
            var consumed = new Dictionary<string, int>();
            var additions = new List<(int Row, OpRecordData Data)>();
            foreach (var row in rows)
            {
                var key = FullRegisterKey(row.Data); var used = consumed.GetValueOrDefault(key);
                consumed[key] = used + 1;
                if (used < exact.GetValueOrDefault(key)) continue;
                if (identities.ContainsKey(RegisterIdentity(row.Data)))
                    throw new ReportValidationException($"La fila {row.Row} coincide con una OP registrada pero sus datos o cantidad de registros cambiaron. Consulta y edita esa OP en el histórico para conservar su identidad; no se duplicaron registros.", 409);
                additions.Add(row);
            }
            db.Add(source);
            foreach (var (row, data) in additions)
            {
                var id = Guid.NewGuid(); data.RegistryId = id; data.RegistryVersion = 1; data.Origin = "Registro histórico";
                db.Add(new ProductionOrderReportRecord
                {
                    Id = id, SourceId = source.Id, SourceRow = row, Number = data.Cells[0].Trim(),
                    Client = data.Cells[5][..Math.Min(500, data.Cells[5].Length)], Product = data.Cells[9][..Math.Min(500, data.Cells[9].Length)], DataJson = JsonSerializer.Serialize(data),
                    CapturedByUserId = user.Id, CapturedAt = clock.GetUtcNow()
                });
            }
            Audit(user, source.Id, "commercial.report.op_register_imported", new { imported = additions.Count, retained = rows.Length - additions.Count, source.Sha256 });
            await Commit(ct); if (transaction is not null) await transaction.CommitAsync(ct);
            return new { imported = additions.Count, duplicate = additions.Count == 0,
                message = $"Informe incorporado: {additions.Count} OP nuevas y {rows.Length - additions.Count} ya registradas. El histórico existente se conserva." };
        }
        finally { PreparationGate.Release(); }
    }

    public async Task<OpRecordDto> SaveOp(Guid? id, SaveOpRecordRequest request, CancellationToken ct)
    {
        var user = await User(ReportPermissionCodes.Edit, ct);
        if (request.Data?.Cells is not { Length: 21 } || request.Data.Cells.Any(c => c is null || c.Length > 32767))
            throw new ReportValidationException("El registro debe contener los 21 campos del Informe de OPs.");
        var cells = request.Data.Cells.Select(c => c.Trim()).ToArray();
        if (cells[0].Length is 0 or > 100)
            throw new ReportValidationException("Completa el número de OP con un máximo de 100 caracteres.");
        ProductionOrderReportRecord? previous = null; OpRecordData? old = null;
        if (id.HasValue)
        {
            previous = await db.Set<ProductionOrderReportRecord>().SingleOrDefaultAsync(r => r.Id == id.Value, ct)
                ?? throw new ReportValidationException("No se encontró la OP.", 404);
            old = RegisterData(previous);
            var active = await ActiveOps(ct);
            if (!active.Any(r => r.Id == previous.Id) || old.SupersededById.HasValue ||
                !DateTimeOffset.TryParse(request.ExpectedCapturedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var expected) || expected.UtcTicks / 10 != previous.CapturedAt.UtcTicks / 10)
                throw new ReportValidationException("El histórico cambió. Recarga esa OP antes de guardar; tus ajustes siguen en pantalla.", 409);
        }
        var nextId = Guid.NewGuid();
        var data = new OpRecordData
        {
            Cells = cells, PortalCode = old?.PortalCode, RegistryId = old?.RegistryId ?? previous?.ProductionOrderId ?? previous?.Id ?? nextId,
            RegistryVersion = old is null ? 1 : old.RegistryVersion + 1, PreviousRecordId = previous?.Id,
            CommercialOverrides = old is null ? [] : old.CommercialOverrides.Concat(
                Enumerable.Range(0, 21).Where(i => cells[i] != old.Cells[i])).Distinct().ToArray(),
            Origin = old?.Origin ?? (previous?.ProductionOrderId.HasValue == true ? "Portal: datos enviados a Producción" : "Registro histórico")
        };
        var next = new ProductionOrderReportRecord
        {
            Id = nextId, Number = cells[0], Code = previous?.Code ?? "", Client = cells[5][..Math.Min(500, cells[5].Length)], Product = cells[9][..Math.Min(500, cells[9].Length)],
            OrderVersion = previous?.OrderVersion,
            DataJson = JsonSerializer.Serialize(data), CapturedByUserId = user.Id, CapturedAt = clock.GetUtcNow()
        };
        if (previous is not null)
        {
            old!.SupersededById = nextId; previous.DataJson = JsonSerializer.Serialize(old);
        }
        db.Add(next); Audit(user, nextId, "commercial.report.op_register_saved", new { previousId = previous?.Id, data.RegistryVersion });
        await Commit(ct); return OpDto(next);
    }
}
