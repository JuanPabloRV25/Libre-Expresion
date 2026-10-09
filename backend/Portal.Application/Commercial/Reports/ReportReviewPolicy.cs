using System.Globalization;
using System.Text.Json;

namespace Portal.Application.Commercial.Reports;

public sealed class ReportReviewData
{
    public int SchemaVersion { get; set; } = 1;
    public int PolicyVersion { get; set; } = 1;
    public List<ReportReviewCase> Cases { get; set; } = [];
    public List<ReportReviewDecision> Decisions { get; set; } = [];
    public List<ReportApproval> Approvals { get; set; } = [];
    public ReportReviewSummary Summary { get; set; } = new();
}

public sealed class ReportReviewCase
{
    public string Id { get; set; } = "";
    public string? RowKey { get; set; }
    public string Scope { get; set; } = "row";
    public string Classification { get; set; } = "automatic";
    public string AutomaticRule { get; set; } = "";
    public string[] DetailIds { get; set; } = [];
    public List<ReportReviewFinding> Findings { get; set; } = [];
}

public sealed class ReportReviewFinding
{
    public string Id { get; set; } = "";
    public string Code { get; set; } = "";
    public string Field { get; set; } = "";
    public string Rule { get; set; } = "";
    public string InitialClassification { get; set; } = "validation";
    public string Resolution { get; set; } = "pending";
    public string Provenance { get; set; } = "automatic";
    public string Proposal { get; set; } = "";
    public string Reason { get; set; } = "";
    public string[] DetailIds { get; set; } = [];
    public ReportProductEvidence[] Evidence { get; set; } = [];
    public ReportSourceRowEvidence[] SourceRows { get; set; } = [];
    public ReportProductEvidence[] ArchivedEvidence { get; set; } = [];
    public bool IsDerivedFromEdit { get; set; }
    public string[] AllowedActions { get; set; } = [];
    public string? DecisionId { get; set; }
    public string DependencyFingerprint { get; set; } = "";
}

public sealed class ReportProductEvidence
{
    public string DetailId { get; set; } = "";
    public string Original { get; set; } = "";
    public string OriginalOp { get; set; } = "";
    public string SaleNumber { get; set; } = "";
    public string Segment { get; set; } = "";
    public string NormalizedProduct { get; set; } = "";
    public ReportOpCandidate[] Candidates { get; set; } = [];
}

public sealed class ReportOpCandidate
{
    public Guid HistoryId { get; set; }
    public string Number { get; set; } = "";
    public string[] MatchedFields { get; set; } = [];
}

public sealed record ReportReviewCommand(string CaseId, string[] FindingIds, string Action,
    Guid[]? HistoryIds = null, string? Op = null);

public sealed class ReportReviewDecision
{
    public string Id { get; set; } = "";
    public string CaseId { get; set; } = "";
    public string[] FindingIds { get; set; } = [];
    public string Action { get; set; } = "";
    public string Before { get; set; } = "";
    public string After { get; set; } = "";
    public Guid[] HistoryIds { get; set; } = [];
    public ReportProductEvidence[] Evidence { get; set; } = [];
    public string Actor { get; set; } = "";
    public DateTimeOffset OccurredAt { get; set; }
    public int ReportVersion { get; set; }
    public string ContentFingerprint { get; set; } = "";
}

public sealed class ReportApproval
{
    public string Actor { get; set; } = "";
    public DateTimeOffset OccurredAt { get; set; }
    public int ReportVersion { get; set; }
    public string ContentFingerprint { get; set; } = "";
    public bool Valid { get; set; } = true;
}

public sealed class ReportReviewSummary
{
    public int SourceRows { get; set; }
    public int FinalRows { get; set; }
    public int AutomaticRows { get; set; }
    public int HumanResolvedRows { get; set; }
    public int ValidationRows { get; set; }
    public int ConflictRows { get; set; }
    public int FilePendingCases { get; set; }
    public int PendingCases { get; set; }
    public int PendingFindings { get; set; }
}

/// <summary>Technical review state over the saved result. Product compatibility is evidence, never an invented ownership rule.</summary>
public static class ReportReviewPolicy
{
    private static readonly string[] OpActions = ["select_candidates", "set_manual_op", "keep_na"];

    public static string ProductSegment(string? detail) => (detail ?? "").Split('/', 2)[0].Trim();

    public static ReportProductEvidence[] ProductEvidence(IEnumerable<SalesDetail> details, IEnumerable<OpRecordDto> history)
    {
        var snapshot = history.ToArray();
        return details.Select(detail =>
        {
            var segment = ProductSegment(detail.Detail);
            var normalized = ReportPreparationEngine.Identity(segment);
            return new ReportProductEvidence
            {
                DetailId = detail.Id, Original = detail.Detail, OriginalOp = detail.ManagerOp,
                SaleNumber = detail.Number, Segment = segment, NormalizedProduct = normalized,
                Candidates = normalized.Length == 0 ? [] : snapshot.Select(record =>
                {
                    var fields = new List<string>();
                    if (ReportPreparationEngine.Identity(record.Data.Cells.ElementAtOrDefault(6)) == normalized) fields.Add("REFERENCIA");
                    if (ReportPreparationEngine.Identity(record.Data.Cells.ElementAtOrDefault(9)) == normalized) fields.Add("PRODUCTO");
                    return new ReportOpCandidate { HistoryId = record.Id, Number = HistoricalNumber(record), MatchedFields = fields.ToArray() };
                }).Where(candidate => candidate.MatchedFields.Length > 0).ToArray()
            };
        }).ToArray();
    }

    public static ReportReviewCase EnsureRowCase(PreparedReportData data, string key, string[] detailIds)
    {
        data.Review ??= new();
        var existing = data.Review.Cases.SingleOrDefault(c => c.RowKey == key);
        if (existing is not null) return existing;
        var result = new ReportReviewCase { Id = SalesReportEngine.Key("review-row-v1", key), RowKey = key, DetailIds = detailIds.ToArray() };
        data.Review.Cases.Add(result);
        return result;
    }

    public static void AddFinding(PreparedReportData data, string? key, string code, string field, string rule,
        string classification, string proposal, string reason, string[] detailIds, ReportProductEvidence[]? evidence = null)
    {
        data.Review ??= new();
        var current = key is not null ? EnsureRowCase(data, key, detailIds) : new ReportReviewCase
        {
            Id = SalesReportEngine.Key("review-file-v1", code, data.Review.Cases.Count.ToString(CultureInfo.InvariantCulture)), Scope = "file"
        };
        if (key is null) data.Review.Cases.Add(current);
        if (current.Findings.Any(f => f.Code == code && f.Field == field)) return;
        current.Findings.Add(new ReportReviewFinding
        {
            Id = SalesReportEngine.Key("review-finding-v1", current.Id, code, field), Code = code, Field = field,
            Rule = rule, InitialClassification = classification, Proposal = proposal, Reason = reason,
            DetailIds = detailIds.ToArray(), Evidence = evidence ?? [], AllowedActions = field == "NUMERO OP" ? OpActions.ToArray() : []
        });
        current.Classification = InitialClassification(current);
    }

    public static void Reconcile(SalesReportData data, PreparedReportData? previous = null, string? actor = null,
        int reportVersion = 0, DateTimeOffset? occurredAt = null)
    {
        var preparation = data.Preparation;
        if (preparation?.Review is null || preparation.RuleVersion < 3) return;
        var review = preparation.Review;
        var newChanges = preparation.Changes.Skip(previous?.Changes.Count ?? preparation.Changes.Count).Where(c => c.Manual).ToArray();
        foreach (var row in preparation.Rows.ToArray())
        {
            var current = EnsureRowCase(preparation, row.Key, row.DetailIds);
            var oldRow = previous?.Rows.SingleOrDefault(r => r.Key == row.Key);
            var restoring = newChanges.Any(c => c.RowKey == row.Key && c.Kind == "manual_restore");
            if (restoring) current.Findings.RemoveAll(f => f.IsDerivedFromEdit);
            if (string.IsNullOrWhiteSpace(row.Line))
            {
                var existed = current.Findings.Any(f => f.Code == "line_missing");
                AddFinding(preparation, row.Key, "line_missing", "LINEA", "required_line", "validation", "", "La línea final está vacía.", row.DetailIds);
                if (!existed && previous is not null) current.Findings.Last(f => f.Code == "line_missing").IsDerivedFromEdit = true;
            }
            if (!row.Amount.HasValue)
            {
                var manual = preparation.RuleVersion >= 4 && row.AmountMode == "manual";
                var code = manual ? "manual_amount_required" : "amount_undetermined";
                var existed = current.Findings.Any(f => f.Code == code);
                AddFinding(preparation, row.Key, code, "VALOR_BRUT", manual ? "manual_multiple_ops" : "existing_amount_required", "validation", "",
                    manual ? "Este registro tiene varias OP. Escribe el valor total bruto para completar el informe." : "VALOR_BRUT final está sin determinar.", row.DetailIds);
                if (!existed && previous is not null) current.Findings.Last(f => f.Code == code).IsDerivedFromEdit = true;
                if (manual && !preparation.AutomaticRows.Any(r => r.Key == row.Key && r.AmountMode == "manual"))
                    current.Findings.Last(f => f.Code == code).IsDerivedFromEdit = true;
            }
            var opContextChanged = oldRow is not null && OpContext(oldRow) != OpContext(row);
            // An edited formerly automatic association must be explicitly decided; it is not a new automatic business rule.
            if (opContextChanged && !restoring && current.Findings.All(f => f.Field != "NUMERO OP"))
            {
                AddFinding(preparation, row.Key, "op_content_changed", "NUMERO OP", "explicit_decision_after_edit", "validation", row.Op,
                    "Cambió un dato del cruce OP. Confirma explícitamente la OP de este registro o conserva N/A.", row.DetailIds);
                current.Findings.Last(f => f.Code == "op_content_changed").IsDerivedFromEdit = true;
            }
            foreach (var finding in current.Findings)
            {
                var fingerprint = Dependency(row, finding.Field);
                if (finding.DependencyFingerprint.Length == 0) finding.DependencyFingerprint = fingerprint;
                if (finding.Resolution == "resolved" && finding.DependencyFingerprint != fingerprint)
                {
                    finding.Resolution = "pending";
                    finding.DecisionId = null;
                }
                if (finding.Code == "manual_amount_required" && row.AmountMode != "manual" ||
                    finding.Code == "amount_undetermined" && row.AmountMode == "automatic" && row.Amount.HasValue)
                {
                    finding.Resolution = "resolved";
                    finding.Provenance = "automatic";
                    finding.DecisionId = null;
                }
                if (finding.Field == "NUMERO OP")
                {
                    finding.Proposal = row.Op;
                    // A changed product/context invalidates current candidate eligibility, retaining original evidence in source and decisions.
                    if (restoring)
                        finding.Evidence = ProductEvidence(data.Details.Where(d => row.DetailIds.Contains(d.Id)), preparation.History);
                    else if (oldRow is not null && (oldRow.Number != row.Number || oldRow.Line != row.Line || !oldRow.Details.SequenceEqual(row.Details)))
                    {
                        finding.ArchivedEvidence = finding.ArchivedEvidence.Concat(finding.Evidence).ToArray();
                        finding.Evidence = [];
                    }
                }
                else if (!restoring && finding.Resolution == "pending" && newChanges.Any(c => c.RowKey == row.Key && c.Field == finding.Field && c.Kind == "manual_edit") && FieldIsDetermined(row, finding))
                {
                    var change = newChanges.Last(c => c.RowKey == row.Key && c.Field == finding.Field && c.Kind == "manual_edit");
                    var decision = new ReportReviewDecision
                    {
                        Id = SalesReportEngine.Key("review-edit-decision", change.Id), CaseId = current.Id, FindingIds = [finding.Id],
                        Action = "field_edit", Before = string.Join(" / ", change.Before), After = change.After, Actor = actor ?? change.Actor ?? "",
                        OccurredAt = occurredAt ?? change.OccurredAt ?? DateTimeOffset.UtcNow, ReportVersion = reportVersion,
                        ContentFingerprint = ContentFingerprint(preparation)
                    };
                    if (review.Decisions.All(d => d.Id != decision.Id)) review.Decisions.Add(decision);
                    Resolve(finding, decision.Id, fingerprint);
                    decision.ContentFingerprint = ContentFingerprint(preparation);
                }
                finding.DependencyFingerprint = fingerprint;
            }
            // Restoring is not confirmation: reinstate the original conditions and discard effective resolutions only for this row.
            if (restoring)
                foreach (var finding in current.Findings)
                {
                    finding.Resolution = "pending";
                    finding.DecisionId = null;
                }
            if (preparation.RuleVersion >= 4)
            {
                var amountFindings = current.Findings.Where(f => f.Field == "VALOR_BRUT").ToArray();
                var reasons = amountFindings.Select(f => f.Reason).ToHashSet();
                var issues = row.Issues.Where(i => !reasons.Contains(i) && (!row.Amount.HasValue || i != "VALOR_BRUT final está sin determinar."))
                    .Concat(amountFindings.Where(f => f.Resolution == "pending").Select(f => f.Reason)).Distinct().ToArray();
                var index = preparation.Rows.FindIndex(r => r.Key == row.Key);
                preparation.Rows[index] = row with { Issues = issues };
            }
            current.Classification = InitialClassification(current);
        }
        InvalidateApprovals(preparation);
        review.Summary = Summary(data);
    }

    public static void ApplyDecisions(SalesReportData data, IEnumerable<ReportReviewCommand> commands,
        string actor, int reportVersion, DateTimeOffset occurredAt)
    {
        var original = data.Preparation ?? throw new ReportValidationException("El reporte no tiene un borrador preparado.");
        if (original.RuleVersion < 3 || original.Review is null) throw new ReportValidationException("Este borrador anterior no tiene revisión estructurada. Prepara uno nuevo con la fuente original.");
        var batch = commands.ToArray();
        if (batch.GroupBy(c => c.CaseId).Any(group => group.Count() > 1))
            throw new ReportValidationException("Un caso solo admite una decisión OP por guardado; no combines acciones contradictorias.");
        if (batch.SelectMany(c => c.FindingIds ?? []).Distinct().Count() != batch.Sum(c => c.FindingIds?.Length ?? 0))
            throw new ReportValidationException("Un hallazgo solo admite una decisión por guardado.");
        // Isolate the complete batch: a later invalid command must not partially confirm an earlier one.
        var next = JsonSerializer.Deserialize<PreparedReportData>(JsonSerializer.Serialize(original))!;
        foreach (var command in batch)
        {
            var current = next.Review!.Cases.SingleOrDefault(c => c.Id == command.CaseId)
                ?? throw new ReportValidationException("El caso no pertenece a este borrador.");
            if (current.RowKey is null || command.FindingIds is null || command.FindingIds.Length == 0)
                throw new ReportValidationException("Selecciona los hallazgos de una fila que quieres resolver.");
            var findings = command.FindingIds.Select(id => current.Findings.SingleOrDefault(f => f.Id == id)
                ?? throw new ReportValidationException("El hallazgo no pertenece al caso seleccionado.")).ToArray();
            if (findings.Any(f => f.Resolution != "pending" || !f.AllowedActions.Contains(command.Action) || f.Field != "NUMERO OP"))
                throw new ReportValidationException("La acción no está permitida para estos hallazgos pendientes.");
            var index = next.Rows.FindIndex(r => r.Key == current.RowKey);
            if (index < 0) throw new ReportValidationException("El caso no tiene un registro final vigente.");
            var row = next.Rows[index];
            string value;
            Guid[] selectedIds = [];
            switch (command.Action)
            {
                case "select_candidates":
                    if (command.Op is not null || command.HistoryIds is null || command.HistoryIds.Length == 0)
                        throw new ReportValidationException("Elige una o varias candidatas, sin combinar la selección con una OP manual.");
                    selectedIds = command.HistoryIds.Distinct().ToArray();
                    var eligible = findings.SelectMany(f => f.Evidence).SelectMany(e => e.Candidates).Select(c => c.HistoryId).ToHashSet();
                    if (selectedIds.Any(id => !eligible.Contains(id))) throw new ReportValidationException("Una candidata elegida no pertenece a este caso y snapshot.");
                    var selected = selectedIds.Select(id => next.History.SingleOrDefault(h => h.Id == id)
                        ?? throw new ReportValidationException("La candidata no pertenece al histórico guardado.")).ToArray();
                    if (selected.Any(h => ReportPreparationEngine.IsUnspecifiedOp(HistoricalNumber(h)))) throw new ReportValidationException("Las candidatas seleccionadas deben tener número OP.");
                    value = string.Join("/", selected.Select(HistoricalNumber).DistinctBy(ReportPreparationEngine.Identity));
                    break;
                case "set_manual_op":
                    if (command.HistoryIds is not null || string.IsNullOrWhiteSpace(command.Op) || ReportPreparationEngine.IsUnspecifiedOp(command.Op))
                        throw new ReportValidationException("Escribe una OP válida. Para confirmar N/A utiliza Mantener N/A.");
                    value = command.Op;
                    break;
                case "keep_na":
                    if (command.HistoryIds is not null || command.Op is not null) throw new ReportValidationException("Mantener N/A no admite una selección ni una OP manual simultánea.");
                    value = "N/A";
                    break;
                default: throw new ReportValidationException("La decisión indicada no está disponible.");
            }
            var updated = row with { Op = value, Modified = true };
            updated = ReportAmountPolicy.Synchronize(data, next, updated, row);
            ReportPreparationEngine.ValidateRow(updated);
            var reasons = findings.Select(f => f.Reason).ToHashSet();
            updated = updated with { Issues = row.Issues.Where(i => !reasons.Contains(i)).ToArray() };
            next.Rows[index] = updated;
            // Keep the actual edit for compatibility. Confirmation itself is recorded separately, even if no cell changed.
            if (row.Op != value)
            {
                var patch = next.RowEdits.SingleOrDefault(e => e.Key == row.Key);
                if (patch is null) { patch = new() { Key = row.Key }; next.RowEdits.Add(patch); }
                patch.Op = value; patch.HistoryIds = null; patch.HistoryReferenceId = null;
                next.Changes.Add(new ReportChange
                {
                    Id = SalesReportEngine.Key("review-op-edit", current.Id, next.Review.Decisions.Count.ToString(CultureInfo.InvariantCulture)),
                    RowKey = row.Key, Kind = "manual_edit", Field = "NUMERO OP", Before = [row.Op], After = value,
                    Reason = "La auxiliar decidió explícitamente las OP del caso.", DetailIds = row.DetailIds.ToArray(), HistoryIds = selectedIds,
                    Actor = actor, Manual = true, ReportVersion = reportVersion, OccurredAt = occurredAt
                });
            }
            var decision = new ReportReviewDecision
            {
                Id = SalesReportEngine.Key("review-decision-v1", current.Id, next.Review.Decisions.Count.ToString(CultureInfo.InvariantCulture), command.Action),
                CaseId = current.Id, FindingIds = command.FindingIds.ToArray(), Action = command.Action, Before = row.Op, After = value,
                HistoryIds = selectedIds, Actor = actor, OccurredAt = occurredAt, ReportVersion = reportVersion,
                Evidence = findings.SelectMany(f => f.Evidence).ToArray(),
                ContentFingerprint = ContentFingerprint(next)
            };
            next.Review.Decisions.Add(decision);
            foreach (var finding in findings) Resolve(finding, decision.Id, Dependency(updated, finding.Field));
            decision.ContentFingerprint = ContentFingerprint(next);
        }
        InvalidateApprovals(next);
        data.Preparation = next;
        Reconcile(data);
    }

    public static void Approve(PreparedReportData data, string actor, int reportVersion, DateTimeOffset occurredAt)
    {
        if (data.RuleVersion < 3 || data.Review is null) throw new ReportValidationException("Prepara un nuevo borrador antes de aprobar esta versión anterior.");
        if (data.Rows.Count == 0 || !HasCompleteCases(data) || data.Review.Cases.Any(c => c.Findings.Any(f => f.Resolution != "resolved")))
            throw new ReportValidationException("Resuelve todos los casos pendientes antes de aprobar VENTAS MES.");
        foreach (var approval in data.Review.Approvals) approval.Valid = false;
        data.Review.Approvals.Add(new() { Actor = actor, ReportVersion = reportVersion, OccurredAt = occurredAt, ContentFingerprint = ContentFingerprint(data) });
    }

    public static bool IsApproved(PreparedReportData data, int? reportVersion = null) => data.RuleVersion >= 3 && data.Rows.Count > 0 && data.Review is not null && HasCompleteCases(data) &&
        data.Review.Cases.All(c => c.Findings.All(f => f.Resolution == "resolved")) && data.Review.Approvals.Any(a =>
            a.Valid && a.ContentFingerprint == ContentFingerprint(data) && (!reportVersion.HasValue || a.ReportVersion == reportVersion.Value));

    public static ReportReviewSummary Summary(SalesReportData data)
    {
        var preparation = data.Preparation;
        var summary = new ReportReviewSummary { SourceRows = data.Details.Count, FinalRows = preparation?.Rows.Count ?? 0 };
        if (preparation?.Review is null) return summary;
        foreach (var row in preparation.Rows)
        {
            var current = preparation.Review.Cases.SingleOrDefault(c => c.RowKey == row.Key);
            // Missing case is never a certificate of automatic success.
            if (current is null || current.Findings.Count == 0 && current.AutomaticRule != "source_singleton_op")
            { summary.ValidationRows++; summary.PendingCases++; summary.PendingFindings++; continue; }
            var pending = current.Findings.Where(f => f.Resolution != "resolved").ToArray();
            if (pending.Length > 0)
            {
                if (pending.Any(f => f.InitialClassification == "conflict")) summary.ConflictRows++; else summary.ValidationRows++;
                summary.PendingCases++; summary.PendingFindings += pending.Length;
            }
            else if (current.Findings.Any(f => f.Provenance == "human") || preparation.RowEdits.Any(e => e.Key == row.Key && HasEffectiveNonFacturaEdit(e))) summary.HumanResolvedRows++;
            else summary.AutomaticRows++;
        }
        foreach (var current in preparation.Review.Cases.Where(c => c.Scope == "file"))
        {
            var count = current.Findings.Count(f => f.Resolution != "resolved");
            if (count == 0) continue;
            summary.FilePendingCases++; summary.PendingCases++; summary.PendingFindings += count;
        }
        return summary;
    }

    public static string ContentFingerprint(PreparedReportData data) => SalesReportEngine.Key("approved-content-v1",
        JsonSerializer.Serialize(data.Rows.Select(r => new { r.Key, r.Op, r.Factura, r.Number, r.Date, r.Client, r.Term, r.Amount, r.Details, r.Line, r.Seller, r.DetailIds })),
        JsonSerializer.Serialize(data.Review?.Cases.Select(c => new { c.Id, c.RowKey, Findings = c.Findings.Select(f => new { f.Id, f.Resolution, f.DecisionId, f.DependencyFingerprint, f.Evidence }) })));

    private static string InitialClassification(ReportReviewCase data) => data.Findings.Any(f => f.InitialClassification == "conflict") ? "conflict" : data.Findings.Count > 0 ? "validation" : "automatic";
    private static bool HasCompleteCases(PreparedReportData data) => data.Review is not null && data.Rows.All(row =>
        data.Review.Cases.Count(c => c.RowKey == row.Key) == 1 && data.Review.Cases.Any(c =>
            c.RowKey == row.Key && c.Scope == "row" && (c.Findings.Count > 0 || c.AutomaticRule == "source_singleton_op")));
    private static string OpContext(SalesGroup row) => SalesReportEngine.Key(row.Op, row.Number, row.Line, string.Join("\n", row.Details));
    private static string Dependency(SalesGroup row, string field) => field == "NUMERO OP" ? OpContext(row)
        : field == "VALOR_BRUT" && row.AmountMode is not null ? SalesReportEngine.Key(field, FieldValue(row, field), row.AmountMode, ReportAmountPolicy.OpSet(row.Op))
        : SalesReportEngine.Key(field, FieldValue(row, field));
    private static string FieldValue(SalesGroup row, string field) => field switch
    {
        "NUMERO OP" => row.Op, "NUMERO" => row.Number, "NOMBRE" => row.Client, "FECHA" => row.Date, "PLAZO" => row.Term,
        "DETALLE" => string.Join("\n", row.Details), "LINEA" => row.Line, "VENDEDOR" => row.Seller,
        "VALOR_BRUT" => row.Amount?.ToString(CultureInfo.InvariantCulture) ?? "", _ => ""
    };
    private static bool FieldIsDetermined(SalesGroup row, ReportReviewFinding finding) => finding.Code switch
    {
        "amount_undetermined" or "manual_amount_required" or "shared_amount_observation" => row.Amount.HasValue,
        "metadata_incompatible" or "line_missing" or "sale_number_missing" or "client_missing" or "detail_missing" => !string.IsNullOrWhiteSpace(FieldValue(row, finding.Field)),
        _ => false
    };
    private static bool HasEffectiveNonFacturaEdit(PreparedRowEdit edit) => edit.Op is not null || edit.HistoryIds is not null ||
        edit.HistoryReferenceId.HasValue || edit.Number is not null || edit.Date is not null || edit.Client is not null ||
        edit.Term is not null || edit.SetAmount || edit.Detail is not null || edit.Line is not null || edit.Seller is not null;
    private static string HistoricalNumber(OpRecordDto row) => row.Data.Cells.ElementAtOrDefault(0) ?? row.Number;
    private static void Resolve(ReportReviewFinding finding, string decisionId, string dependency)
    {
        finding.Resolution = "resolved"; finding.Provenance = "human"; finding.DecisionId = decisionId; finding.DependencyFingerprint = dependency;
    }
    private static void InvalidateApprovals(PreparedReportData data)
    {
        if (data.Review is null) return;
        var fingerprint = ContentFingerprint(data);
        foreach (var approval in data.Review.Approvals.Where(a => a.Valid && a.ContentFingerprint != fingerprint)) approval.Valid = false;
    }
}
