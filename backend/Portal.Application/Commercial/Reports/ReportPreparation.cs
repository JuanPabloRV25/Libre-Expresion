using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Portal.Application.Commercial.Reports;

public sealed class PreparedReportData
{
    public int RuleVersion { get; set; } = 2;
    public string Status { get; set; } = "generated";
    public string HistoryFingerprint { get; set; } = "";
    public string IdentityKey { get; set; } = "";
    public List<OpRecordDto> History { get; set; } = [];
    public List<SalesGroup> AutomaticRows { get; set; } = [];
    public List<SalesGroup> Rows { get; set; } = [];
    public List<ReportChange> Changes { get; set; } = [];
    public List<PreparedRowEdit> RowEdits { get; set; } = [];
    public ReportReviewData? Review { get; set; }
}

public sealed class ReportChange
{
    public string Id { get; set; } = "";
    public string? RowKey { get; set; }
    public string Kind { get; set; } = "";
    public string Field { get; set; } = "";
    public string[] Before { get; set; } = [];
    public string After { get; set; } = "";
    public string Reason { get; set; } = "";
    public string[] DetailIds { get; set; } = [];
    public Guid[] HistoryIds { get; set; } = [];
    public string? Actor { get; set; }
    public bool Manual { get; set; }
    public int ReportVersion { get; set; }
    public DateTimeOffset? OccurredAt { get; set; }
}

public sealed class PreparedRowEdit
{
    public string Key { get; set; } = "";
    public string? Op { get; set; }
    public string? Factura { get; set; }
    public string? Number { get; set; }
    public string? Date { get; set; }
    public string? Client { get; set; }
    public string? Term { get; set; }
    public decimal? Amount { get; set; }
    public bool SetAmount { get; set; }
    public string? Detail { get; set; }
    public string? Line { get; set; }
    public string? Seller { get; set; }
    public Guid[]? HistoryIds { get; set; }
    public Guid? HistoryReferenceId { get; set; }
    public bool Restore { get; set; }
}

/// <summary>The result is materialized once; reopening/exporting must not consult a newer history.</summary>
public static class ReportPreparationEngine
{
    public const int ExcelCellLimit = 32767;
    public const int CurrentRuleVersion = 4;
    public const string DefaultFactura = "F01";
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    // Keep accents, punctuation and meaningful leading zeroes when deciding identities.
    public static string Identity(string? value) => Regex.Replace((value ?? "").Trim(), @"\s+", " ").ToUpperInvariant();

    public static string HistoryFingerprint(IEnumerable<OpRecordDto> history) => SalesReportEngine.Key(
        JsonSerializer.Serialize(history.OrderBy(h => h.Id)));

    public static PreparedReportData Prepare(SalesReportData data, List<OpRecordDto> history)
    {
        var result = new PreparedReportData
        {
            RuleVersion = CurrentRuleVersion,
            HistoryFingerprint = HistoryFingerprint(history),
            History = history.Select(CloneHistory).ToList(),
            Review = new ReportReviewData()
        };
        Add(result, null, "columns_removed", "Columnas", ["IVA", "TOTAL", "NUMERO_REM"], "",
            "IVA, TOTAL y NUMERO_REM se excluyen porque no forman parte de VENTAS MES.", []);
        foreach (var warning in data.Warnings)
        {
            Add(result, null, "observation", "Archivo", [], "", warning, []);
            ReportReviewPolicy.AddFinding(result, null, "source_warning", "Archivo", "source_integrity", "validation", "", warning, []);
            var finding = result.Review!.Cases.Last().Findings.Last();
            finding.SourceRows = data.SourceWarningEvidence.Where(e => e.Warning == warning)
                .SelectMany(e => e.Rows).ToArray();
        }

        var buckets = data.Details.GroupBy(d => IsUnspecifiedOp(d.ManagerOp) || Identity(d.Line).Length == 0 || Identity(d.Number).Length == 0
            ? SalesReportEngine.Key("independent", d.Id)
            : SalesReportEngine.Key("sale-number-line", Identity(d.Number), Identity(d.ManagerOp), Identity(d.Line)));
        foreach (var bucket in buckets)
        {
            var source = bucket.ToArray();
            var first = source[0];
            var ids = source.Select(d => d.Id).ToArray();
            var key = SalesReportEngine.Key($"prepared-v{CurrentRuleVersion}", bucket.Key);
            var issues = new List<string>();
            var op = first.ManagerOp;
            Guid[] historyIds = [];
            var reviewCase = ReportReviewPolicy.EnsureRowCase(result, key, ids);
            if (source.Length == 1 && !IsUnspecifiedOp(op)) reviewCase.AutomaticRule = "source_singleton_op";
            if (IsUnspecifiedOp(op))
            {
                op = "N/A";
                Observe("NUMERO OP", "OP no informada o no determinada; conserva N/A hasta una decisión de la auxiliar.", code: "op_not_informed");
            }
            if (Identity(first.Line).Length == 0)
                Observe("LINEA", "La línea está vacía; no se agrupa esta fila con otras ventas.", code: "line_missing");
            if (Identity(first.Number).Length == 0)
                Observe("NUMERO", "NUMERO está vacío; se conserva independiente sin asociarlo a otra venta.", code: "sale_number_missing");

            if (source.Length > 1)
            {
                Add(result, key, "rows_consolidated", "Filas", source.Select(d => $"{d.SourceFile} · {d.Sheet} · fila {d.SourceRow}").ToArray(),
                    "Una fila", "Las filas comparten NUMERO, NUMERO_OP y LINEA dentro de la misma venta; se reúnen sus detalles.", ids);
                op = "N/A";
                Observe("NUMERO OP", "La coincidencia del producto identifica candidatas, pero no demuestra su pertenencia a esta venta. Confirma las OP o conserva N/A.", code: "op_relationship_undetermined");
                Add(result, key, "details_combined", "DETALLE", source.Select(d => d.Detail).ToArray(), string.Join("\n", source.Select(d => d.Detail)),
                    "Se conservan todos los detalles de ventas, en su orden de origen y separados por saltos de línea.", ids, historyIds);
            }
            else
            {
                Add(result, key, "source_kept", "NUMERO OP", [first.ManagerOp], op,
                    "Esta combinación de número y línea tiene una sola fila; pasa directamente sin exigir asociación histórica.", ids);
            }

            var number = Common("NUMERO", d => d.Number);
            var date = Common("FECHA", d => d.Date);
            var client = Common("NOMBRE", d => d.Client);
            var term = Common("PLAZO", d => d.Term);
            var seller = Common("VENDEDOR", d => d.Seller);
            if (source.Any(d => string.IsNullOrWhiteSpace(d.Client)))
                Observe("NOMBRE", "Una fila de ventas no tiene NOMBRE; se conserva el dato vacío y su procedencia.", code: "client_missing");
            if (source.Any(d => string.IsNullOrWhiteSpace(d.Detail)))
                Observe("DETALLE", "Una fila de ventas no tiene DETALLE; no se inventó un texto para completarla.", code: "detail_missing");
            decimal? amount = source.Select(d => d.RawAmount).Distinct().Count() == 1 ? first.RawAmount : null;
            if (!amount.HasValue)
                Observe("VALOR_BRUT", "Hay importes de origen distintos; VALOR_BRUT queda vacío para evitar escoger o sumar un valor sin evidencia.", code: "amount_undetermined", classification: "conflict");
            else if (source.Length > 1)
                Add(result, key, "amount_kept_once", "VALOR_BRUT", source.Select(d => Money(d.RawAmount)).ToArray(), Money(amount),
                    "El mismo VALOR_BRUT se repite en el grupo y se conserva una sola vez; no se suman las repeticiones.", ids);

            var row = new SalesGroup(key, "", number, date, op, "", client, term, amount, DefaultFactura, first.Line, seller,
                source.Select(d => d.Detail).ToArray(), ids, issues.ToArray(), false);
            row = ReportAmountPolicy.Synchronize(data, result, row);
            ValidateRow(row);
            result.Rows.Add(row);

            string Common(string field, Func<SalesDetail, string> get)
            {
                var values = source.Select(get).ToArray();
                if (values.Select(Identity).Distinct().Count() == 1) return values[0];
                Observe(field, $"{field} contiene valores diferentes en las filas de origen; el campo final queda vacío.", before: values, code: "metadata_incompatible", classification: "conflict");
                return "";
            }
            void Observe(string field, string reason, Guid[]? evidence = null, string[]? before = null, string code = "field_requires_validation", string classification = "validation")
            {
                issues.Add(reason);
                Add(result, key, "observation", field, before ?? SourceValues(source, field), field == "NUMERO OP" ? op : "", reason, ids, evidence);
                ReportReviewPolicy.AddFinding(result, key, code, field, code, classification,
                    field == "NUMERO OP" ? op : "", reason, ids,
                    field == "NUMERO OP" ? ReportReviewPolicy.ProductEvidence(source, result.History) : []);
            }
        }

        // Repeated amounts across different lines may be shared totals, but equality alone is not proof.
        var detailById = data.Details.ToDictionary(d => d.Id);
        var rowsByNumber = result.Rows.Where(r => r.DetailIds.Length > 0 && !IsUnspecifiedOp(detailById[r.DetailIds[0]].ManagerOp))
            .GroupBy(r => SalesReportEngine.Key(Identity(detailById[r.DetailIds[0]].Number), Identity(detailById[r.DetailIds[0]].ManagerOp)));
        var rowIndices = result.Rows.Select((row, index) => (row.Key, index)).ToDictionary(x => x.Key, x => x.index);
        foreach (var number in rowsByNumber)
        {
            var matchingRows = number.ToArray();
            if (matchingRows.Length < 2 || matchingRows.Select(r => Identity(r.Line)).Distinct().Count() < 2) continue;
            foreach (var sameAmount in matchingRows.Where(r => r.Amount.HasValue).GroupBy(r => r.Amount).Where(g => g.Count() > 1))
                foreach (var row in sameAmount)
                {
                    const string reason = "El mismo número OP aparece en líneas distintas con el mismo importe; podría ser un total compartido. Se conserva el origen sin repartirlo automáticamente.";
                    Add(result, row.Key, "shared_amount_observation", "VALOR_BRUT", [Money(row.Amount)], Money(row.Amount), reason, row.DetailIds);
                    ReportReviewPolicy.AddFinding(result, row.Key, "shared_amount_observation", "VALOR_BRUT", "existing_shared_amount_warning", "validation", Money(row.Amount), reason, row.DetailIds);
                    var index = rowIndices[row.Key];
                    result.Rows[index] = row with { Issues = row.Issues.Append(reason).ToArray() };
                }
        }
        result.AutomaticRows = result.Rows.Select(CloneRow).ToList();
        UpdateStatus(result);
        data.Preparation = result;
        ReportReviewPolicy.Reconcile(data);
        return result;
    }

    public static OpRecordDto[] ExpandHistoryReference(PreparedReportData preparation, Guid anchorId)
    {
        var anchor = preparation.History.SingleOrDefault(h => h.Id == anchorId)
            ?? throw new ReportValidationException("La referencia histórica no pertenece a este reporte.");
        if (!HasReference(anchor)) throw new ReportValidationException("La referencia histórica debe tener completos F y G.");
        return preparation.History.Where(h => HasReference(h) && ReferenceKey(h) == ReferenceKey(anchor) && Identity(HistoricalNumber(h)).Length > 0).ToArray();
    }

    public static PreparedReportData Apply(SalesReportData data, IEnumerable<PreparedRowEdit> edits, string actor)
    {
        var preparation = data.Preparation ?? throw new ReportValidationException("Este reporte utiliza las reglas anteriores.");
        var patches = edits.ToArray();
        if (patches.Select(e => e.Key).Distinct().Count() != patches.Length)
            throw new ReportValidationException("Una fila solo puede aparecer una vez en el ajuste.");
        // Work on an isolated snapshot so a later invalid patch cannot partially mutate a valid report.
        var result = JsonSerializer.Deserialize<PreparedReportData>(JsonSerializer.Serialize(preparation))!;
        // Store one effective patch per row, including data saved by an earlier version of this engine.
        result.RowEdits = result.RowEdits.GroupBy(e => e.Key)
            .Select(group => group.Aggregate((PreparedRowEdit?)null, (saved, edit) => MergeEdit(result, saved, edit))!).ToList();
        foreach (var edit in patches)
        {
            if (result.RuleVersion >= 3 && (edit.HistoryReferenceId.HasValue || edit.HistoryIds is not null))
                throw new ReportValidationException("Confirma las OP con una decisión explícita del caso; la expansión histórica anterior no se aplica a este borrador.");
            var index = result.Rows.FindIndex(r => r.Key == edit.Key);
            if (index < 0) throw new ReportValidationException("La fila que intentas editar no pertenece a este reporte.");
            var previous = result.Rows[index];
            if (edit.Restore)
            {
                var automatic = result.AutomaticRows.Single(r => r.Key == edit.Key);
                var hadManualDecision = result.RowEdits.Any(e => e.Key == edit.Key);
                result.Rows[index] = CloneRow(automatic);
                result.RowEdits.RemoveAll(e => e.Key == edit.Key);
                var fields = new[]
                {
                    ("NUMERO OP", previous.Op, automatic.Op), ("FACTURA", previous.Factura, automatic.Factura),
                    ("NUMERO", previous.Number, automatic.Number), ("FECHA", previous.Date, automatic.Date),
                    ("NOMBRE", previous.Client, automatic.Client), ("PLAZO", previous.Term, automatic.Term),
                    ("VALOR_BRUT", Money(previous.Amount), Money(automatic.Amount)),
                    ("DETALLE", string.Join("\n", previous.Details), string.Join("\n", automatic.Details)),
                    ("LINEA", previous.Line, automatic.Line), ("VENDEDOR", previous.Seller, automatic.Seller)
                };
                foreach (var (field, before, after) in fields.Where(f => f.Item2 != f.Item3))
                    Add(result, edit.Key, "manual_restore", field, [before], after,
                        "La auxiliar recuperó la propuesta automática de este campo.", previous.DetailIds, actor: actor);
                if (fields.All(f => f.Item2 == f.Item3) && (hadManualDecision || previous.Modified || !previous.Issues.SequenceEqual(automatic.Issues)))
                    Add(result, edit.Key, "manual_restore", "Fila", [], "Propuesta automática recuperada",
                        "La auxiliar recuperó la propuesta automática de la fila y sus observaciones.", previous.DetailIds, actor: actor);
                continue;
            }
            var row = previous;
            var changedFields = new HashSet<string>();
            Guid[] historyIds = [];
            if (edit.HistoryIds is not null && edit.HistoryReferenceId.HasValue)
                throw new ReportValidationException("Elige una referencia histórica o un conjunto de OP, no ambos a la vez.");
            var historicalSelection = edit.HistoryReferenceId.HasValue
                ? ExpandHistoryReference(result, edit.HistoryReferenceId.Value).Select(h => h.Id).ToArray()
                : edit.HistoryIds;
            if (historicalSelection is not null && edit.Op is not null && edit.Op != previous.Op)
                throw new ReportValidationException("Elige una referencia histórica o escribe el número OP manualmente, no ambas decisiones a la vez.");
            if (historicalSelection is not null)
            {
                if (historicalSelection.Length == 0) throw new ReportValidationException("Selecciona al menos una OP histórica o escribe el número OP manualmente.");
                var historyById = result.History.ToDictionary(h => h.Id);
                var selected = historicalSelection.Distinct().Select(id => historyById.GetValueOrDefault(id)
                    ?? throw new ReportValidationException("Una OP elegida no pertenece al histórico guardado de este reporte.")).ToArray();
                if (selected.Any(h => Identity(HistoricalNumber(h)).Length == 0))
                    throw new ReportValidationException("Completa el número de las OP seleccionadas.");
                historyIds = selected.Select(h => h.Id).ToArray();
                var saved = result.RowEdits.SingleOrDefault(e => e.Key == row.Key);
                var currentHistoryIds = saved is { HistoryReferenceId: not null } || saved?.HistoryIds is not null
                    ? EditHistoryIds(result, saved!)
                    : saved?.Op is not null ? []
                    : result.Changes.LastOrDefault(c => c.RowKey == row.Key && !c.Manual && c.Kind == "op_rectified")?.HistoryIds ?? [];
                var associationChanged = !historyIds.ToHashSet().SetEquals(currentHistoryIds);
                Patch("NUMERO OP", row.Op, Numbers(selected), value => row = row with { Op = value }, "La auxiliar eligió explícitamente el conjunto de OP del histórico guardado.", historyIds,
                    force: associationChanged || HasPendingObservation("NUMERO OP"));
            }
            if (historicalSelection is null && edit.Op is not null) Patch("NUMERO OP", row.Op, edit.Op, v => row = row with { Op = v });
            if (edit.Factura is not null) Patch("FACTURA", row.Factura, edit.Factura, v => row = row with { Factura = v });
            if (edit.Number is not null) Patch("NUMERO", row.Number, edit.Number, v => row = row with { Number = v });
            if (edit.Date is not null)
            {
                if (edit.Date.Length > 0 && !DateTime.TryParseExact(edit.Date, "yyyy-MM-dd", Invariant, DateTimeStyles.None, out _))
                    throw new ReportValidationException("FECHA debe ser una fecha válida o quedar vacía.");
                Patch("FECHA", row.Date, edit.Date, v => row = row with { Date = v });
            }
            if (edit.Client is not null) Patch("NOMBRE", row.Client, edit.Client, v => row = row with { Client = v });
            if (edit.Term is not null) Patch("PLAZO", row.Term, edit.Term, v => row = row with { Term = v });
            row = ReportAmountPolicy.Synchronize(data, result, row, previous);
            if (edit.SetAmount)
            {
                if (result.RuleVersion >= 4) ReportAmountPolicy.ValidateEdit(row, edit);
                var next = edit.Amount;
                if (next.HasValue && (next.Value < -999999999999999m || next.Value > 999999999999999m))
                    throw new ReportValidationException("VALOR_BRUT supera la precisión permitida para Excel.");
                var confirmsObservation = HasPendingObservation("VALOR_BRUT");
                Patch("VALOR_BRUT", Money(row.Amount), Money(next), _ => row = row with { Amount = next },
                    reason: row.Amount == next && confirmsObservation ? "La auxiliar confirmó VALOR_BRUT y conservó el importe mostrado." : null,
                    force: confirmsObservation);
            }
            if (edit.Detail is not null) Patch("DETALLE", string.Join("\n", row.Details), edit.Detail, v => row = row with { Details = [v] });
            if (edit.Line is not null) Patch("LINEA", row.Line, edit.Line, v => row = row with { Line = v });
            if (edit.Seller is not null) Patch("VENDEDOR", row.Seller, edit.Seller, v => row = row with { Seller = v });

            var resolvedReasons = result.Changes.Where(c => !c.Manual && c.RowKey == row.Key && changedFields.Contains(c.Field) &&
                c.Kind is "observation" or "shared_amount_observation").Select(c => c.Reason).ToHashSet();
            string[] missingReasons = ["El número OP final está vacío.", "La línea final está vacía.", "VALOR_BRUT final está sin determinar."];
            row = row with { Issues = row.Issues.Where(issue => !resolvedReasons.Contains(issue) && !missingReasons.Contains(issue)).ToArray(), Modified = changedFields.Count > 0 || row.Modified };
            // Clearing an uncertain value is allowed; its missing state remains explicit.
            var missing = new List<string>();
            if (string.IsNullOrWhiteSpace(row.Op)) missing.Add("El número OP final está vacío.");
            if (string.IsNullOrWhiteSpace(row.Line)) missing.Add("La línea final está vacía.");
            if (!row.Amount.HasValue) missing.Add("VALOR_BRUT final está sin determinar.");
            row = row with { Issues = row.Issues.Concat(missing).Distinct().ToArray() };
            ValidateRow(row);
            result.Rows[index] = row;
            if (changedFields.Count > 0)
            {
                var saved = result.RowEdits.SingleOrDefault(e => e.Key == edit.Key);
                var merged = MergeEdit(result, saved, edit, changedFields);
                result.RowEdits.RemoveAll(e => e.Key == edit.Key);
                result.RowEdits.Add(merged);
            }

            bool HasPendingObservation(string field) => result.Changes.Any(c => !c.Manual && c.RowKey == row.Key && c.Field == field &&
                (c.Kind is "observation" or "shared_amount_observation") && row.Issues.Contains(c.Reason)) ||
                result.RuleVersion >= 4 && result.Review?.Cases.Any(c => c.RowKey == row.Key &&
                    c.Findings.Any(f => f.Field == field && f.Resolution == "pending")) == true;

            void Patch(string field, string before, string after, Action<string> apply, string? reason = null, Guid[]? evidence = null, bool force = false)
            {
                if (!force && before == after) return;
                ValidateText(after, field);
                apply(after);
                changedFields.Add(field);
                Add(result, row.Key, "manual_edit", field, [before], after, reason ?? $"La auxiliar ajustó {field} del resultado; el dato original permanece conservado.",
                    row.DetailIds, evidence, actor);
            }
        }
        UpdateStatus(result);
        data.Preparation = result;
        return result;
    }

    private static Guid[] EditHistoryIds(PreparedReportData result, PreparedRowEdit edit) => edit.HistoryReferenceId.HasValue
        ? ExpandHistoryReference(result, edit.HistoryReferenceId.Value).Select(h => h.Id).ToArray()
        : edit.HistoryIds ?? [];

    private static PreparedRowEdit MergeEdit(PreparedReportData result, PreparedRowEdit? saved, PreparedRowEdit incoming, HashSet<string>? changedFields = null)
    {
        bool Changed(string field) => changedFields is null || changedFields.Contains(field);
        string? Value(string field, string? next, string? previous) => Changed(field) && next is not null ? next : previous;
        var merged = new PreparedRowEdit
        {
            Key = incoming.Key, Op = saved?.Op, HistoryReferenceId = saved?.HistoryReferenceId, HistoryIds = saved?.HistoryIds?.ToArray(),
            Factura = Value("FACTURA", incoming.Factura, saved?.Factura), Number = Value("NUMERO", incoming.Number, saved?.Number),
            Date = Value("FECHA", incoming.Date, saved?.Date), Client = Value("NOMBRE", incoming.Client, saved?.Client),
            Term = Value("PLAZO", incoming.Term, saved?.Term), Detail = Value("DETALLE", incoming.Detail, saved?.Detail),
            Line = Value("LINEA", incoming.Line, saved?.Line), Seller = Value("VENDEDOR", incoming.Seller, saved?.Seller),
            SetAmount = incoming.SetAmount && Changed("VALOR_BRUT") || saved?.SetAmount == true,
            Amount = incoming.SetAmount && Changed("VALOR_BRUT") ? incoming.Amount : saved?.Amount
        };
        if (!Changed("NUMERO OP")) return merged;
        if (incoming.HistoryReferenceId.HasValue)
        {
            merged.HistoryReferenceId = incoming.HistoryReferenceId; merged.HistoryIds = null; merged.Op = null;
        }
        else if (incoming.HistoryIds is not null)
        {
            merged.HistoryIds = incoming.HistoryIds.Distinct().ToArray(); merged.HistoryReferenceId = null; merged.Op = null;
        }
        else if (incoming.Op is not null)
        {
            // Old full-form patches could repeat the displayed historical number without changing the association.
            var repeatsHistoricalNumber = false;
            if (changedFields is null && saved is not null && (saved.HistoryReferenceId.HasValue || saved.HistoryIds is not null))
            {
                var historyById = result.History.ToDictionary(h => h.Id);
                repeatsHistoricalNumber = incoming.Op == Numbers(EditHistoryIds(result, saved).Select(id => historyById[id]));
            }
            if (!repeatsHistoricalNumber)
            {
                merged.Op = incoming.Op; merged.HistoryIds = null; merged.HistoryReferenceId = null;
            }
        }
        return merged;
    }

    public static bool IncompleteTotals(PreparedReportData preparation) => preparation.Rows.Any(r => !r.Amount.HasValue ||
        preparation.Changes.Any(c => c.RowKey == r.Key && c.Kind == "shared_amount_observation" && r.Issues.Contains(c.Reason))) ||
        preparation.RuleVersion >= 4 && preparation.Review?.Cases.Any(c =>
            c.Findings.Any(f => f.Field == "VALOR_BRUT" && f.Resolution == "pending")) == true;

    public static void ValidateRow(SalesGroup row)
    {
        string[] values = [row.Op, row.Factura, row.Number, row.Date, row.Client, row.Term, string.Join("\n", row.Details), row.Line, row.Seller];
        foreach (var value in values) ValidateText(value, "El contenido de una celda");
    }

    private static void ValidateText(string value, string field)
    {
        if (value.Length > ExcelCellLimit) throw new ReportValidationException($"{field} supera los 32.767 caracteres que admite una celda de Excel. Conserva los detalles en el archivo de origen y reduce o separa el contenido antes de preparar.");
        if (value.Any(c => c is < '\x20' and not '\t' and not '\r' and not '\n'))
            throw new ReportValidationException($"{field} contiene caracteres que Excel no puede representar.");
    }
    private static void UpdateStatus(PreparedReportData result) => result.Status = result.Rows.Any(r => r.Issues.Length > 0) ||
        result.Changes.Any(c => c.RowKey is null && c.Kind == "observation") ? "generated_with_observations" : "generated";
    private static string HistoricalNumber(OpRecordDto h) => h.Data.Cells.ElementAtOrDefault(0) ?? h.Number;
    public static bool IsUnspecifiedOp(string? op) => Identity(op) is "" or "N/A" or "NA";
    private static bool HasReference(OpRecordDto h) => Identity(h.Data.Cells.ElementAtOrDefault(5)).Length > 0 && Identity(h.Data.Cells.ElementAtOrDefault(6)).Length > 0;
    private static string ReferenceKey(OpRecordDto h) => SalesReportEngine.Key(Identity(h.Data.Cells.ElementAtOrDefault(5)), Identity(h.Data.Cells.ElementAtOrDefault(6)));
    private static string Numbers(IEnumerable<OpRecordDto> selected) => string.Join(" / ", selected.Select(HistoricalNumber).DistinctBy(Identity));
    private static string Money(decimal? amount) => amount?.ToString(Invariant) ?? "";
    private static OpRecordDto CloneHistory(OpRecordDto h) => h with { Data = new OpRecordData
    {
        Cells = h.Data.Cells.ToArray(), PortalCode = h.Data.PortalCode, RegistryId = h.Data.RegistryId,
        RegistryVersion = h.Data.RegistryVersion, PreviousRecordId = h.Data.PreviousRecordId,
        SupersededById = h.Data.SupersededById, Origin = h.Data.Origin, CommercialOverrides = h.Data.CommercialOverrides.ToArray()
    } };
    private static SalesGroup CloneRow(SalesGroup row) => row with { Details = row.Details.ToArray(), DetailIds = row.DetailIds.ToArray(), Issues = row.Issues.ToArray() };
    private static string[] SourceValues(SalesDetail[] source, string field) => source.Select(d => field switch
    {
        "NUMERO OP" => d.ManagerOp, "LINEA" => d.Line, "VALOR_BRUT" => Money(d.RawAmount), "NUMERO" => d.Number,
        "FECHA" => d.Date, "NOMBRE" => d.Client, "PLAZO" => d.Term, "VENDEDOR" => d.Seller, _ => ""
    }).ToArray();
    private static void Add(PreparedReportData result, string? row, string kind, string field, string[] before,
        string after, string reason, string[] details, Guid[]? history = null, string? actor = null)
    {
        result.Changes.Add(new ReportChange
        {
            Id = SalesReportEngine.Key("change", result.Changes.Count.ToString(Invariant), row ?? "", kind, field, after),
            RowKey = row, Kind = kind, Field = field, Before = before, After = after, Reason = reason,
            DetailIds = details.ToArray(), HistoryIds = history ?? [], Actor = actor, Manual = actor is not null
        });
    }
}
