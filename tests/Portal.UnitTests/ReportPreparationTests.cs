using Portal.Application.Commercial.Reports;

namespace Portal.UnitTests;

public class ReportPreparationTests
{
    private static SalesDetail Sale(string id, string op = "4500", string line = "Línea A", string? detail = null,
        decimal amount = 100m, string number = "9100", string client = "Cliente de ventas") => new()
    {
        Id = id, SourceId = Guid.NewGuid(), SourceFile = "Ventas.xlsx", Sheet = "Hoja1", SourceRow = int.Parse(id) + 1,
        ManagerOp = op, Line = line, Detail = detail ?? "Detalle " + id, RawAmount = amount, Number = number,
        Date = "2026-10-07", Client = client, Term = "30", Seller = "Comercial A", DocumentId = "legacy-document-" + id
    };

    private static OpRecordDto Historical(string number, string f = "Nombre histórico", string g = "Detalle histórico")
    {
        var cells = new string[21];
        cells[0] = number; cells[5] = f; cells[6] = g; cells[9] = "Producto histórico distinto de Manager";
        return new(Guid.NewGuid(), number, "OP-2026-001", f, cells[9], 1, DateTimeOffset.UtcNow, "Importado", new() { Cells = cells });
    }

    [Fact]
    public void A_unique_op_passes_directly_even_when_history_has_an_unrelated_reference()
    {
        var data = new SalesReportData { Details = [Sale("1")] };
        var result = ReportPreparationEngine.Prepare(data, [Historical("4500"), Historical("4501")]);
        var row = Assert.Single(result.Rows);
        Assert.Equal("4500", row.Op);
        Assert.Equal("Cliente de ventas", row.Client);
        Assert.Equal("F01", row.Factura);
        Assert.Empty(row.Issues);
        Assert.DoesNotContain(result.Changes, c => c.Kind == "op_rectified");
        Assert.False(data.Details[0].Reviewed);
        Assert.Null(data.Details[0].SelectedOpId);
    }

    [Fact]
    public void Repeated_op_and_line_preserve_every_detail_but_do_not_certify_historical_ownership()
    {
        var data = new SalesReportData { Details = [Sale("1"), Sale("2", detail: "Detalle diferente"), Sale("3", detail: "Detalle diferente")] };
        var history = new List<OpRecordDto> { Historical("4500"), Historical("4501"), Historical("4502"), Historical("9000", g: "Otra referencia") };
        var result = ReportPreparationEngine.Prepare(data, history);
        var row = Assert.Single(result.Rows);
        Assert.Equal("N/A", row.Op);
        Assert.Equal(new[] { "Detalle 1", "Detalle diferente", "Detalle diferente" }, row.Details);
        Assert.Equal(100m, row.Amount);
        Assert.Equal("Cliente de ventas", row.Client);
        Assert.Equal(new[] { "1", "2", "3" }, row.DetailIds);
        Assert.DoesNotContain(result.Changes, c => c.Kind == "op_rectified");
        Assert.Single(result.Changes, c => c.Kind == "columns_removed");
        Assert.NotEmpty(row.Issues);
        Assert.Equal(4, result.RuleVersion);
        Assert.Equal(1, result.Review!.Summary.ValidationRows);
    }

    [Fact]
    public void Different_lines_remain_separate_and_a_singleton_combination_does_not_expand_history()
    {
        var data = new SalesReportData { Details = [Sale("1"), Sale("2"), Sale("3", line: "Línea B", amount: 20)] };
        var result = ReportPreparationEngine.Prepare(data, [Historical("4500"), Historical("4501")]);
        Assert.Equal(2, result.Rows.Count);
        Assert.Equal("N/A", result.Rows[0].Op);
        Assert.Equal("4500", result.Rows[1].Op);
        Assert.Equal(new[] { "Detalle 3" }, result.Rows[1].Details);
    }

    [Fact]
    public void Missing_numbers_or_lines_stay_independent_and_never_expand_empty_historical_references()
    {
        var data = new SalesReportData { Details = [Sale("1", op: ""), Sale("2", op: ""), Sale("3", line: ""), Sale("4", line: "")] };
        var result = ReportPreparationEngine.Prepare(data, [Historical("4500", f: "", g: ""), Historical("4501", f: "", g: "")]);
        Assert.Equal(4, result.Rows.Count);
        Assert.All(result.Rows, row => Assert.NotEmpty(row.Issues));
        Assert.DoesNotContain(result.Changes, c => c.Kind == "op_rectified");
        Assert.Equal("generated_with_observations", result.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Missing_or_ambiguous_historical_ownership_remains_NA_for_explicit_validation(bool ambiguous)
    {
        var history = ambiguous ? new List<OpRecordDto> { Historical("4500"), Historical("4500", g: "Otra referencia") } : [];
        var data = new SalesReportData { Details = [Sale("1"), Sale("2")] };
        var result = ReportPreparationEngine.Prepare(data, history);
        var row = Assert.Single(result.Rows);
        Assert.Equal("N/A", row.Op);
        Assert.NotEmpty(row.Issues);
        Assert.DoesNotContain(result.Changes, c => c.Kind == "op_rectified");
        Assert.Equal(2, row.Details.Length);
    }

    [Fact]
    public void Different_amounts_and_metadata_are_not_summed_or_selected_from_the_first_row()
    {
        var data = new SalesReportData { Details = [Sale("1"), Sale("2", amount: 25, client: "Otro cliente")] };
        var row = Assert.Single(ReportPreparationEngine.Prepare(data, [Historical("4500")]).Rows);
        Assert.Null(row.Amount);
        Assert.Equal("", row.Client);
        Assert.Equal("9100", row.Number);
        Assert.Equal("2026-10-07", row.Date);
        Assert.Contains(row.Issues, issue => issue.Contains("importes de origen distintos"));
        Assert.Equal("Cliente de ventas", data.Details[0].Client);
        Assert.Equal("Otro cliente", data.Details[1].Client);
    }

    [Fact]
    public void Equal_amounts_in_different_lines_only_mark_shared_totals_for_the_same_source_op()
    {
        var repeated = new SalesReportData { Details = [Sale("1"), Sale("2", line: "Línea B")] };
        var repeatedResult = ReportPreparationEngine.Prepare(repeated, []);
        Assert.All(repeatedResult.Rows, row => Assert.Equal(100m, row.Amount));
        Assert.True(ReportPreparationEngine.IncompleteTotals(repeatedResult));
        Assert.Equal(2, repeatedResult.Changes.Count(c => c.Kind == "shared_amount_observation"));

        var independent = new SalesReportData { Details = [Sale("1"), Sale("2", op: "4600", line: "Línea B")] };
        var independentResult = ReportPreparationEngine.Prepare(independent, []);
        Assert.False(ReportPreparationEngine.IncompleteTotals(independentResult));
        Assert.DoesNotContain(independentResult.Changes, c => c.Kind == "shared_amount_observation");
    }

    [Theory]
    [InlineData("ÁRBOL", "ARBOL")]
    [InlineData("A-B", "AB")]
    [InlineData("0012", "12")]
    public void Identity_preserves_accents_punctuation_and_leading_zeroes(string left, string right)
    {
        Assert.NotEqual(ReportPreparationEngine.Identity(left), ReportPreparationEngine.Identity(right));
        Assert.Equal(ReportPreparationEngine.Identity("  Línea    A  "), ReportPreparationEngine.Identity("línea a"));
    }

    [Fact]
    public void The_history_snapshot_and_automatic_proposal_are_not_mutated_by_later_registry_changes_or_edits()
    {
        var anchor = Historical("4500");
        anchor.Data.RegistryId = Guid.NewGuid();
        anchor.Data.RegistryVersion = 3;
        anchor.Data.PreviousRecordId = Guid.NewGuid();
        anchor.Data.Origin = "Registro histórico";
        anchor.Data.CommercialOverrides = [0, 5, 6];
        var second = Historical("4501");
        var data = new SalesReportData { Details = [Sale("1"), Sale("2")] };
        var result = ReportPreparationEngine.Prepare(data, [anchor, second]);
        var key = result.Rows[0].Key;
        anchor.Data.Cells[6] = "Referencia cambiada después";
        Assert.Equal("Detalle histórico", result.History[0].Data.Cells[6]);
        Assert.Equal(anchor.Data.RegistryId, result.History[0].Data.RegistryId);
        Assert.Equal(3, result.History[0].Data.RegistryVersion);
        Assert.Equal(anchor.Data.PreviousRecordId, result.History[0].Data.PreviousRecordId);
        Assert.Equal("Registro histórico", result.History[0].Data.Origin);
        Assert.Equal(new[] { 0, 5, 6 }, result.History[0].Data.CommercialOverrides);
        anchor.Data.CommercialOverrides[0] = 4;
        Assert.Equal(0, result.History[0].Data.CommercialOverrides[0]);
        var edit = ReportPreparationEngine.Apply(data, [new() { Key = key, Client = "Ajuste manual", Factura = "Texto de auxiliar" }], "auxiliar");
        Assert.Equal("Ajuste manual", edit.Rows[0].Client);
        Assert.Equal("Texto de auxiliar", edit.Rows[0].Factura);
        Assert.Equal("Cliente de ventas", edit.AutomaticRows[0].Client);
        Assert.Equal("Cliente de ventas", data.Details[0].Client);
        Assert.Contains(edit.Changes, c => c.Manual && c.Actor == "auxiliar" && c.Before.SequenceEqual(new[] { "Cliente de ventas" }) && c.After == "Ajuste manual");
        var restored = ReportPreparationEngine.Apply(data, [new() { Key = key, Restore = true }], "auxiliar");
        Assert.Equal("Cliente de ventas", restored.Rows[0].Client);
        Assert.Equal("F01", restored.Rows[0].Factura);
        Assert.Empty(restored.RowEdits);
        Assert.Contains(restored.Changes, c => c.Kind == "manual_restore");
    }

    [Fact]
    public void Manual_historical_reference_uses_the_saved_snapshot_and_records_its_evidence()
    {
        var first = Historical("4500");
        var second = Historical("4501");
        var ambiguous = Historical("4500", g: "Otra referencia");
        var data = new SalesReportData { Details = [Sale("1"), Sale("2")] };
        var result = ReportPreparationEngine.Prepare(data, [first, second, ambiguous]);
        result.RuleVersion = 2; result.Review = null; // Explicit saved V2 compatibility; no new preparation uses F/G expansion.
        Assert.NotEmpty(result.Rows[0].Issues);
        first.Data.Cells[6] = "Cambio después";
        var corrected = ReportPreparationEngine.Apply(data, [new() { Key = result.Rows[0].Key, HistoryReferenceId = first.Id }], "auxiliar");
        Assert.Equal("4500 / 4501", corrected.Rows[0].Op);
        Assert.Empty(corrected.Rows[0].Issues);
        var change = Assert.Single(corrected.Changes, c => c.Manual && c.Field == "NUMERO OP");
        Assert.Equal(new[] { first.Id, second.Id }, change.HistoryIds);
    }

    [Fact]
    public void A_deliberate_zero_amount_is_preserved_and_an_optional_blank_is_not_converted_to_zero()
    {
        var data = new SalesReportData { Details = [Sale("1"), Sale("2", amount: 25)] };
        var prepared = ReportPreparationEngine.Prepare(data, [Historical("4500")]);
        var key = prepared.Rows[0].Key;
        var zero = ReportPreparationEngine.Apply(data, [new() { Key = key, Amount = 0m, SetAmount = true }], "auxiliar");
        ReportReviewPolicy.Reconcile(data, prepared, "auxiliar");
        Assert.Equal(0m, zero.Rows[0].Amount);
        Assert.False(ReportPreparationEngine.IncompleteTotals(zero));
        var blank = ReportPreparationEngine.Apply(data, [new() { Key = key, Amount = null, SetAmount = true }], "auxiliar");
        ReportReviewPolicy.Reconcile(data, zero, "auxiliar");
        Assert.Null(blank.Rows[0].Amount);
        Assert.True(ReportPreparationEngine.IncompleteTotals(blank));
        var completed = ReportPreparationEngine.Apply(data, [new() { Key = key, Amount = 10m, SetAmount = true }], "auxiliar");
        ReportReviewPolicy.Reconcile(data, blank, "auxiliar");
        Assert.DoesNotContain(completed.Rows[0].Issues, issue => issue.Contains("sin determinar"));
    }

    [Fact]
    public void A_failed_edit_batch_leaves_the_saved_result_unchanged()
    {
        var data = new SalesReportData { Details = [Sale("1"), Sale("2", op: "4600")] };
        var original = ReportPreparationEngine.Prepare(data, []);
        Assert.Throws<ReportValidationException>(() => ReportPreparationEngine.Apply(data,
            [new() { Key = original.Rows[0].Key, Client = "No guardar" }, new() { Key = original.Rows[1].Key, Date = "No es una fecha" }], "auxiliar"));
        Assert.Same(original, data.Preparation);
        Assert.NotNull(data.Preparation);
        Assert.Equal("Cliente de ventas", data.Preparation.Rows[0].Client);
        Assert.DoesNotContain(data.Preparation.Changes, c => c.Manual);
    }

    [Fact]
    public void Excel_limits_fail_explicitly_without_truncating_original_details()
    {
        var detail = new string('A', 20000);
        var data = new SalesReportData { Details = [Sale("1", detail: detail), Sale("2", detail: detail)] };
        var error = Assert.Throws<ReportValidationException>(() => ReportPreparationEngine.Prepare(data, [Historical("4500")]));
        Assert.Contains("32.767", error.Message);
        Assert.Equal(detail, data.Details[0].Detail);
        Assert.Equal(detail, data.Details[1].Detail);
        Assert.Null(data.Preparation);
    }

    [Fact]
    public void Duplicate_historical_numbers_keep_every_candidate_without_automatic_selection()
    {
        var data = new SalesReportData { Details = [Sale("1", detail: "Detalle histórico / A"), Sale("2", detail: "Detalle histórico / B")] };
        var history = new List<OpRecordDto> { Historical("4500"), Historical("4501"), Historical("4501") };
        var result = ReportPreparationEngine.Prepare(data, history);
        Assert.Equal("N/A", result.Rows[0].Op);
        var finding = Assert.Single(result.Review!.Cases[0].Findings);
        Assert.All(finding.Evidence, evidence => Assert.Equal(3, evidence.Candidates.Length));
    }

    [Fact]
    public void Historical_product_J_is_optional_but_no_F_G_expansion_certifies_a_new_report()
    {
        var first = Historical("4500") with { Product = "" };
        var second = Historical("4501") with { Product = "" };
        first.Data.Cells[9] = "";
        second.Data.Cells[9] = "";
        var data = new SalesReportData { Details = [Sale("1"), Sale("2")] };
        var result = ReportPreparationEngine.Prepare(data, [first, second]);
        Assert.Equal("N/A", result.Rows[0].Op);
        Assert.NotEmpty(result.Rows[0].Issues);
    }

    [Fact]
    public void The_history_fingerprint_is_order_independent_but_includes_registry_version_lineage()
    {
        var first = Historical("4500");
        var second = Historical("4501");
        var fingerprint = ReportPreparationEngine.HistoryFingerprint([first, second]);
        Assert.Equal(fingerprint, ReportPreparationEngine.HistoryFingerprint([second, first]));
        first.Data.RegistryVersion++;
        first.Data.PreviousRecordId = Guid.NewGuid();
        Assert.NotEqual(fingerprint, ReportPreparationEngine.HistoryFingerprint([first, second]));
    }

    [Fact]
    public void Consecutive_row_deltas_merge_into_one_saved_patch_and_restoring_one_row_keeps_the_other()
    {
        var data = new SalesReportData { Details = [Sale("1"), Sale("2", op: "4600/4601")] };
        var original = ReportPreparationEngine.Prepare(data, []);
        var a = original.Rows[0].Key; var b = original.Rows[1].Key;
        PreparedReportData Save(params PreparedRowEdit[] edits)
        {
            var previous = data.Preparation!;
            var result = ReportPreparationEngine.Apply(data, edits, "auxiliar");
            ReportReviewPolicy.Reconcile(data, previous, "auxiliar");
            return result;
        }
        Save(new PreparedRowEdit { Key = a, Factura = "FE01" });
        Save(new PreparedRowEdit { Key = a, Client = "Nombre corregido" });
        var saved = Save(new PreparedRowEdit { Key = b, Amount = 80m, SetAmount = true });
        Assert.Equal(2, saved.RowEdits.Count);
        Assert.Equal(2, saved.RowEdits.Select(e => e.Key).Distinct().Count());
        var patchA = Assert.Single(saved.RowEdits, e => e.Key == a);
        Assert.Equal("FE01", patchA.Factura);
        Assert.Equal("Nombre corregido", patchA.Client);
        Assert.Equal(3, saved.Changes.Count(c => c.Manual));

        var repeated = Save(new PreparedRowEdit { Key = a, Client = "Nombre corregido" }, new PreparedRowEdit { Key = b, Amount = 80m, SetAmount = true });
        Assert.Equal(3, repeated.Changes.Count(c => c.Manual));
        Assert.Equal(2, repeated.RowEdits.Count);
        var restored = Save(new PreparedRowEdit { Key = a, Restore = true });
        Assert.Equal("F01", restored.Rows.Single(r => r.Key == a).Factura);
        Assert.Equal("Cliente de ventas", restored.Rows.Single(r => r.Key == a).Client);
        Assert.Equal(80m, restored.Rows.Single(r => r.Key == b).Amount);
        Assert.Equal(b, Assert.Single(restored.RowEdits).Key);
        var count = restored.Changes.Count;
        var restoredAgain = Save(new PreparedRowEdit { Key = a, Restore = true });
        Assert.Equal(count, restoredAgain.Changes.Count);
    }

    [Fact]
    public void Canonical_patches_keep_historical_and_manual_number_decisions_mutually_exclusive()
    {
        var first = Historical("4500"); var second = Historical("4501");
        var data = new SalesReportData { Details = [Sale("1"), Sale("2")] };
        var key = ReportPreparationEngine.Prepare(data, [first, second, Historical("4500", g: "Otra referencia")]).Rows[0].Key;
        data.Preparation!.RuleVersion = 2; data.Preparation.Review = null;
        ReportPreparationEngine.Apply(data, [new() { Key = key, HistoryReferenceId = first.Id }], "auxiliar");
        var named = ReportPreparationEngine.Apply(data, [new() { Key = key, Client = "Nombre corregido" }], "auxiliar");
        var patch = Assert.Single(named.RowEdits);
        Assert.Equal(first.Id, patch.HistoryReferenceId);
        Assert.Null(patch.HistoryIds);
        Assert.Null(patch.Op);
        Assert.Equal("Nombre corregido", patch.Client);

        var selected = ReportPreparationEngine.Apply(data, [new() { Key = key, HistoryIds = [second.Id] }], "auxiliar");
        patch = Assert.Single(selected.RowEdits);
        Assert.Null(patch.HistoryReferenceId);
        Assert.Equal(new[] { second.Id }, patch.HistoryIds);
        Assert.Null(patch.Op);
        var manual = ReportPreparationEngine.Apply(data, [new() { Key = key, Op = "9000" }], "auxiliar");
        patch = Assert.Single(manual.RowEdits);
        Assert.Equal("9000", patch.Op);
        Assert.Null(patch.HistoryReferenceId);
        Assert.Null(patch.HistoryIds);
        Assert.Equal("Nombre corregido", patch.Client);
        var chosenAgain = ReportPreparationEngine.Apply(data, [new() { Key = key, HistoryReferenceId = first.Id }], "auxiliar");
        patch = Assert.Single(chosenAgain.RowEdits);
        Assert.Null(patch.Op);
        Assert.Null(patch.HistoryIds);
        Assert.Equal(first.Id, patch.HistoryReferenceId);
    }

    [Fact]
    public void Confirming_an_observed_amount_once_clears_the_observation_but_repetition_does_not_fabricate_changes()
    {
        var data = new SalesReportData { Details = [Sale("1"), Sale("2", line: "Línea B")] };
        var prepared = ReportPreparationEngine.Prepare(data, []);
        var a = prepared.Rows[0].Key; var b = prepared.Rows[1].Key;
        var confirmed = ReportPreparationEngine.Apply(data, [new() { Key = a, Amount = 100m, SetAmount = true }], "auxiliar");
        ReportReviewPolicy.Reconcile(data, prepared, "auxiliar");
        Assert.Equal(1, confirmed.Changes.Count(c => c.Manual));
        Assert.DoesNotContain(confirmed.Rows[0].Issues, issue => issue.Contains("total compartido"));
        Assert.True(ReportPreparationEngine.IncompleteTotals(confirmed));
        var repeated = ReportPreparationEngine.Apply(data, [new() { Key = a, Amount = 100m, SetAmount = true }], "auxiliar");
        ReportReviewPolicy.Reconcile(data, confirmed, "auxiliar");
        Assert.Equal(1, repeated.Changes.Count(c => c.Manual));
        var complete = ReportPreparationEngine.Apply(data, [new() { Key = b, Amount = 100m, SetAmount = true }], "auxiliar");
        ReportReviewPolicy.Reconcile(data, repeated, "auxiliar");
        Assert.False(ReportPreparationEngine.IncompleteTotals(complete));
        Assert.Equal(2, complete.Changes.Count(c => c.Manual));
    }

    [Fact]
    public void Repeating_a_resolved_historical_selection_does_not_add_an_audit_or_patch()
    {
        var first = Historical("4500"); var second = Historical("4501");
        var data = new SalesReportData { Details = [Sale("1"), Sale("2")] };
        var prepared = ReportPreparationEngine.Prepare(data, [first, second]);
        prepared.RuleVersion = 2; prepared.Review = null;
        var key = prepared.Rows[0].Key;
        var unchanged = ReportPreparationEngine.Apply(data, [new() { Key = key, HistoryReferenceId = first.Id }], "auxiliar");
        Assert.Single(unchanged.RowEdits);
        Assert.Single(unchanged.Changes, c => c.Manual);
        var sameSelection = ReportPreparationEngine.Apply(data, [new() { Key = key, HistoryReferenceId = first.Id }], "auxiliar");
        Assert.Single(sameSelection.Changes, c => c.Manual);

        var uniqueData = new SalesReportData { Details = [Sale("1")] };
        var uniqueKey = ReportPreparationEngine.Prepare(uniqueData, [first]).Rows[0].Key;
        uniqueData.Preparation!.RuleVersion = 2; uniqueData.Preparation.Review = null;
        var associated = ReportPreparationEngine.Apply(uniqueData, [new() { Key = uniqueKey, HistoryIds = [first.Id] }], "auxiliar");
        Assert.Single(associated.Changes, c => c.Manual);
        var repeated = ReportPreparationEngine.Apply(uniqueData, [new() { Key = uniqueKey, HistoryIds = [first.Id] }], "auxiliar");
        Assert.Single(repeated.Changes, c => c.Manual);
        Assert.Single(repeated.RowEdits);
    }

    [Fact]
    public void Earlier_accumulated_patches_are_normalized_without_replaying_the_saved_result()
    {
        var data = new SalesReportData { Details = [Sale("1")] };
        var prepared = ReportPreparationEngine.Prepare(data, []);
        var key = prepared.Rows[0].Key;
        prepared.Rows[0] = prepared.Rows[0] with { Factura = "FE01", Client = "Nombre guardado", Modified = true };
        prepared.RowEdits = [new() { Key = key, Factura = "FE01" }, new() { Key = key, Client = "Nombre guardado" }];
        var normalized = ReportPreparationEngine.Apply(data, [], "auxiliar");
        var patch = Assert.Single(normalized.RowEdits);
        Assert.Equal("FE01", patch.Factura);
        Assert.Equal("Nombre guardado", patch.Client);
        Assert.Equal("FE01", normalized.Rows[0].Factura);
        Assert.Equal("Nombre guardado", normalized.Rows[0].Client);
        Assert.DoesNotContain(normalized.Changes, c => c.Manual);
    }

    [Theory]
    [InlineData("00017")]
    [InlineData("  Texto libre de la auxiliar  ")]
    [InlineData("")]
    public void New_preparations_default_invoice_to_F01_and_preserve_explicit_text_or_blank_until_restore(string invoice)
    {
        var data = new SalesReportData { Details = [Sale("1"), Sale("2", op: "4600")] };
        var prepared = ReportPreparationEngine.Prepare(data, []);
        Assert.Equal(4, prepared.RuleVersion);
        Assert.All(prepared.Rows, row => Assert.Equal("F01", row.Factura));
        Assert.All(prepared.AutomaticRows, row => Assert.Equal("F01", row.Factura));
        var key = prepared.Rows[0].Key;

        var edited = ReportPreparationEngine.Apply(data, [new() { Key = key, Factura = invoice }], "auxiliar");
        Assert.Equal(invoice, edited.Rows.Single(row => row.Key == key).Factura);
        Assert.Equal(invoice, Assert.Single(edited.RowEdits).Factura);
        var changedOp = ReportPreparationEngine.Apply(data, [new() { Key = key, Op = "9000" }], "auxiliar");
        Assert.Equal(invoice, changedOp.Rows.Single(row => row.Key == key).Factura);
        var changedName = ReportPreparationEngine.Apply(data, [new() { Key = key, Client = "Cliente corregido" }], "auxiliar");
        Assert.Equal(invoice, changedName.Rows.Single(row => row.Key == key).Factura);
        Assert.Equal(invoice, Assert.Single(changedName.RowEdits).Factura);
        Assert.Equal("F01", changedName.AutomaticRows.Single(row => row.Key == key).Factura);
        Assert.Equal("F01", changedName.Rows.Single(row => row.Key != key).Factura);

        var restored = ReportPreparationEngine.Apply(data, [new() { Key = key, Restore = true }], "auxiliar");
        Assert.Equal("F01", restored.Rows.Single(row => row.Key == key).Factura);
        Assert.Empty(restored.RowEdits);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Factura de versión anterior")]
    public void Saved_V3_invoice_defaults_are_completed_before_editing_and_manual_values_survive_until_restore(string savedInvoice)
    {
        var data = new SalesReportData { Details = [Sale("1")] };
        var prepared = ReportPreparationEngine.Prepare(data, []);
        // A saved V3 proposal has a blank automatic invoice; opening it now
        // supplies the default without changing its OP or amount rules.
        prepared.RuleVersion = 3;
        prepared.AutomaticRows[0] = prepared.AutomaticRows[0] with { Factura = "" };
        prepared.Rows[0] = prepared.Rows[0] with { Factura = savedInvoice, Modified = savedInvoice.Length > 0 };
        var key = prepared.Rows[0].Key;
        if (savedInvoice.Length > 0) prepared.RowEdits = [new() { Key = key, Factura = savedInvoice }];
        ReportInvoicePolicy.ApplyDefaults(data);

        var edited = ReportPreparationEngine.Apply(data, [new() { Key = key, Client = "Nombre actualizado" }], "auxiliar");
        Assert.Equal(3, edited.RuleVersion);
        Assert.Equal(savedInvoice.Length == 0 ? "F01" : savedInvoice, edited.Rows[0].Factura);
        Assert.Equal("F01", edited.AutomaticRows[0].Factura);
        var restored = ReportPreparationEngine.Apply(data, [new() { Key = key, Restore = true }], "auxiliar");
        Assert.Equal(3, restored.RuleVersion);
        Assert.Equal("F01", restored.Rows[0].Factura);
    }
}
