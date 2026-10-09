using System.Text.Json;
using Portal.Application.Commercial.Reports;

namespace Portal.UnitTests;

public class ReportReviewPolicyTests
{
    private static readonly DateTimeOffset When = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
    private static SalesDetail Sale(string id, string op = "4500", string number = "8889", string detail = "Producto A / detalle", string line = "EMPAQUE", decimal amount = 100) => new()
    {
        Id = id, Number = number, ManagerOp = op, Detail = detail, Line = line, RawAmount = amount,
        Date = "2026-10-07", Client = "Cliente", SourceRow = int.Parse(id), SourceFile = "ventas.xlsx", Sheet = "Ventas"
    };
    private static OpRecordDto Historical(string number, string reference = "Producto A", string product = "Producto A")
    {
        var cells = new string[21]; cells[0] = number; cells[5] = "Cliente"; cells[6] = reference; cells[7] = "EMPAQUE"; cells[9] = product;
        return new(Guid.NewGuid(), number, "", "Cliente", product, null, When, "Histórico", new() { Cells = cells });
    }
    private static SalesReportData Prepare(List<SalesDetail> details, params OpRecordDto[] history)
    {
        var data = new SalesReportData { Details = details };
        ReportPreparationEngine.Prepare(data, history.ToList());
        return data;
    }
    private static ReportReviewCase Case(SalesReportData data, string? key = null) => data.Preparation!.Review!.Cases.Single(c => c.RowKey == (key ?? data.Preparation.Rows[0].Key));
    private static ReportReviewCommand OpCommand(SalesReportData data, string action, Guid[]? ids = null, string? op = null, string? key = null)
    {
        var current = Case(data, key);
        return new(current.Id, current.Findings.Where(f => f.Field == "NUMERO OP" && f.Resolution == "pending").Select(f => f.Id).ToArray(), action, ids, op);
    }
    private static void Patch(SalesReportData data, PreparedRowEdit edit, int version = 2)
    {
        var previous = JsonSerializer.Deserialize<PreparedReportData>(JsonSerializer.Serialize(data.Preparation))!;
        ReportPreparationEngine.Apply(data, [edit], "auxiliar");
        ReportReviewPolicy.Reconcile(data, previous, "auxiliar", version, When);
    }

    [Fact]
    public void Different_sales_are_never_consolidated_or_given_a_cross_sale_shared_amount_warning()
    {
        var data = Prepare([Sale("1", number: "8889"), Sale("2", number: "8890"), Sale("3", number: "8891", line: "DIGITAL")]);
        Assert.Equal(3, data.Preparation!.Rows.Count);
        Assert.Equal(new[] { "8889", "8890", "8891" }, data.Preparation.Rows.Select(r => r.Number));
        Assert.All(data.Preparation.Rows, row => Assert.Equal(100, row.Amount));
        Assert.DoesNotContain(data.Preparation.Changes, c => c.Kind == "shared_amount_observation");
        Assert.Equal(3, ReportReviewPolicy.Summary(data).AutomaticRows);
    }

    [Theory]
    [InlineData("  Producto    Á-001 / primero / segundo", "Producto    Á-001", "PRODUCTO Á-001")]
    [InlineData(" Producto Á-001 ", "Producto Á-001", "PRODUCTO Á-001")]
    [InlineData("/ nada", "", "")]
    public void Product_extraction_has_conservative_normalization(string original, string segment, string identity)
    {
        Assert.Equal(segment, ReportReviewPolicy.ProductSegment(original));
        Assert.Equal(identity, ReportPreparationEngine.Identity(ReportReviewPolicy.ProductSegment(original)));
    }

    [Fact]
    public void Reference_or_product_match_preserves_every_record_and_one_candidate_when_both_columns_match()
    {
        var both = Historical("001", "Producto A", "Producto A");
        var reference = Historical("002", " producto   a ", "Otro");
        var product = Historical("003", "Otra referencia", "PRODUCTO A");
        var reused = Historical("004", "Producto A", "Producto A");
        var fuzzy = Historical("005", "Producto-A", "Producto A grande");
        var data = Prepare([Sale("1"), Sale("2")], both, reference, product, reused, fuzzy);
        var finding = Assert.Single(Case(data).Findings);
        Assert.Equal("N/A", data.Preparation!.Rows[0].Op);
        Assert.All(finding.Evidence, evidence =>
        {
            Assert.Equal("8889", evidence.SaleNumber);
            Assert.Equal(4, evidence.Candidates.Length);
            Assert.Equal(new[] { "REFERENCIA", "PRODUCTO" }, evidence.Candidates.Single(c => c.HistoryId == both.Id).MatchedFields);
            Assert.DoesNotContain(evidence.Candidates, c => c.HistoryId == fuzzy.Id);
        });
        Assert.Equal("validation", finding.InitialClassification);
        Assert.Equal("pending", finding.Resolution);
    }

    [Fact]
    public void Missing_source_op_is_NA_validation_even_when_one_product_candidate_exists()
    {
        var data = Prepare([Sale("1", op: ""), Sale("2", op: "")], Historical("001"));
        Assert.Equal(2, data.Preparation!.Rows.Count);
        Assert.All(data.Preparation.Rows, r => Assert.Equal("N/A", r.Op));
        Assert.All(data.Preparation.Review!.Cases, c =>
        {
            var finding = Assert.Single(c.Findings);
            Assert.Equal("op_not_informed", finding.Code);
            Assert.Equal("validation", finding.InitialClassification);
            Assert.Single(Assert.Single(finding.Evidence).Candidates);
        });
        Assert.Equal(2, ReportReviewPolicy.Summary(data).ValidationRows);
    }

    [Fact]
    public void Known_three_products_are_resolved_by_explicit_selection_and_every_detail_is_preserved()
    {
        var a = Historical("25273", "CAJA 50 ML", "CAJA 50 ML");
        var b = Historical("25279", "CAJA 100 ML", "CAJA 100 ML");
        var c = Historical("25280", "CAJA 250 ML", "CAJA 250 ML");
        var data = Prepare([Sale("1", "25280", "8877", "CAJA 50 ML / detalle A", amount: 823500), Sale("2", "25280", "8877", "CAJA 100 ML / detalle B", amount: 823500), Sale("3", "25280", "8877", "CAJA 250 ML / detalle C", amount: 823500)], a, b, c);
        Assert.Equal("N/A", data.Preparation!.Rows[0].Op);
        ReportReviewPolicy.ApplyDecisions(data, [OpCommand(data, "select_candidates", [a.Id, b.Id, c.Id])], "auxiliar", 2, When);
        Assert.Equal("25273/25279/25280", data.Preparation.Rows[0].Op);
        Assert.Null(data.Preparation.Rows[0].Amount);
        Assert.Equal(1, ReportReviewPolicy.Summary(data).PendingCases);
        Patch(data, new() { Key = data.Preparation.Rows[0].Key, SetAmount = true, Amount = 823500 }, 3);
        Assert.Equal(823500, data.Preparation.Rows[0].Amount);
        Assert.Equal(3, data.Preparation.Rows[0].Details.Length);
        Assert.Equal(3, Assert.Single(data.Preparation.Review!.Decisions, d => d.Action == "select_candidates").Evidence.Length);
        Assert.Equal(1, ReportReviewPolicy.Summary(data).HumanResolvedRows);
        Assert.Equal(0, ReportReviewPolicy.Summary(data).PendingCases);
    }

    [Theory]
    [InlineData("keep_na", null, "N/A")]
    [InlineData("set_manual_op", "000999 externa", "000999 externa")]
    public void Explicit_NA_or_manual_decision_is_persisted_with_human_provenance(string action, string? manual, string expected)
    {
        var data = Prepare([Sale("1", op: "")]);
        var before = data.Preparation!.Rows[0].Op;
        ReportReviewPolicy.ApplyDecisions(data, [OpCommand(data, action, op: manual)], "auxiliar-7", 8, When);
        var saved = JsonSerializer.Deserialize<SalesReportData>(JsonSerializer.Serialize(data))!;
        var finding = Assert.Single(Case(saved).Findings);
        Assert.Equal("validation", finding.InitialClassification);
        Assert.Equal("resolved", finding.Resolution);
        Assert.Equal("human", finding.Provenance);
        Assert.Equal(expected, saved.Preparation!.Rows[0].Op);
        var decision = Assert.Single(saved.Preparation.Review!.Decisions);
        Assert.Equal("auxiliar-7", decision.Actor);
        Assert.Equal(When, decision.OccurredAt);
        Assert.Equal(8, decision.ReportVersion);
        Assert.Equal(before, decision.Before);
        if (action == "keep_na") Assert.DoesNotContain(saved.Preparation.Changes, c => c.Manual);
        Assert.Equal(1, ReportReviewPolicy.Summary(saved).HumanResolvedRows);
    }

    [Fact]
    public void A_candidate_from_another_case_or_contradictory_command_is_rejected_without_partial_changes()
    {
        var a = Historical("001"); var b = Historical("002", "Producto B", "Producto B");
        var data = Prepare([Sale("1", op: ""), Sale("2", op: "", detail: "Producto B / detalle")], a, b);
        var original = data.Preparation;
        var good = OpCommand(data, "select_candidates", [a.Id]);
        var wrong = OpCommand(data, "select_candidates", [a.Id], key: data.Preparation!.Rows[1].Key);
        Assert.Throws<ReportValidationException>(() => ReportReviewPolicy.ApplyDecisions(data, [good, wrong], "auxiliar", 2, When));
        Assert.Same(original, data.Preparation);
        Assert.Empty(original!.Review!.Decisions);
        Assert.Throws<ReportValidationException>(() => ReportReviewPolicy.ApplyDecisions(data, [OpCommand(data, "keep_na", [a.Id])], "auxiliar", 2, When));
        Assert.Throws<ReportValidationException>(() => ReportReviewPolicy.ApplyDecisions(data, [OpCommand(data, "set_manual_op", op: "N/A")], "auxiliar", 2, When));
    }

    [Fact]
    public void Resolving_OP_does_not_resolve_amount_conflicts_and_row_counts_remain_a_partition()
    {
        var data = Prepare([Sale("1"), Sale("2", amount: 50), Sale("3", "9000", "9999")]);
        var row = data.Preparation!.Rows[0];
        Assert.Equal(1, ReportReviewPolicy.Summary(data).ConflictRows);
        ReportReviewPolicy.ApplyDecisions(data, [OpCommand(data, "keep_na")], "auxiliar", 2, When);
        Assert.Equal(1, ReportReviewPolicy.Summary(data).ConflictRows);
        Assert.Equal(1, ReportReviewPolicy.Summary(data).AutomaticRows);
        Assert.Equal(1, ReportReviewPolicy.Summary(data).PendingCases);
        Assert.Equal(1, ReportReviewPolicy.Summary(data).PendingFindings);
        Assert.Throws<ReportValidationException>(() => ReportReviewPolicy.Approve(data.Preparation!, "auxiliar", 3, When));
        Patch(data, new() { Key = row.Key, Amount = 75, SetAmount = true }, 3);
        Assert.Equal(75, data.Preparation.Rows[0].Amount);
        Assert.Equal(1, ReportReviewPolicy.Summary(data).HumanResolvedRows);
        Assert.Equal(0, ReportReviewPolicy.Summary(data).PendingCases);
        Assert.Equal(2, ReportReviewPolicy.Summary(data).AutomaticRows + ReportReviewPolicy.Summary(data).HumanResolvedRows + ReportReviewPolicy.Summary(data).ValidationRows + ReportReviewPolicy.Summary(data).ConflictRows);
    }

    [Fact]
    public void FACTURA_never_resolves_OP_and_after_confirmation_does_not_reopen_it_but_invalidates_approval()
    {
        var data = Prepare([Sale("1", op: "")]); var key = data.Preparation!.Rows[0].Key;
        Patch(data, new() { Key = key, Factura = "00012-A" });
        Assert.Equal(1, ReportReviewPolicy.Summary(data).PendingCases);
        ReportReviewPolicy.ApplyDecisions(data, [OpCommand(data, "keep_na")], "auxiliar", 3, When);
        ReportReviewPolicy.Approve(data.Preparation!, "auxiliar", 4, When);
        Assert.True(ReportReviewPolicy.IsApproved(data.Preparation!, 4));
        Assert.False(ReportReviewPolicy.IsApproved(data.Preparation!, 3));
        Patch(data, new() { Key = key, Factura = "00012-B" }, 5);
        Assert.Equal("00012-B", data.Preparation.Rows[0].Factura);
        Assert.Equal(0, ReportReviewPolicy.Summary(data).PendingCases);
        Assert.False(ReportReviewPolicy.IsApproved(data.Preparation));
        Assert.False(Assert.Single(data.Preparation.Review!.Approvals).Valid);
    }

    [Fact]
    public void Direct_OP_edit_is_not_confirmation_and_edited_context_reopens_only_affected_case()
    {
        var data = Prepare([Sale("1", op: ""), Sale("2", op: "", number: "8890")]);
        var a = data.Preparation!.Rows[0].Key; var b = data.Preparation.Rows[1].Key;
        ReportReviewPolicy.ApplyDecisions(data, [OpCommand(data, "keep_na", key: a), OpCommand(data, "keep_na", key: b)], "auxiliar", 2, When);
        Patch(data, new() { Key = a, Op = "999" });
        Assert.Equal("pending", Assert.Single(Case(data, a).Findings).Resolution);
        Assert.Equal("resolved", Assert.Single(Case(data, b).Findings).Resolution);
        Assert.Equal(1, ReportReviewPolicy.Summary(data).ValidationRows);
        Assert.Equal(1, ReportReviewPolicy.Summary(data).HumanResolvedRows);
    }

    [Fact]
    public void Editing_product_context_removes_current_eligibility_but_retains_decision_and_original_evidence()
    {
        var history = Historical("001"); var data = Prepare([Sale("1", op: "")], history);
        ReportReviewPolicy.ApplyDecisions(data, [OpCommand(data, "select_candidates", [history.Id])], "auxiliar", 2, When);
        Patch(data, new() { Key = data.Preparation!.Rows[0].Key, Detail = "Producto B / corregido" });
        var finding = Assert.Single(Case(data).Findings);
        Assert.Equal("pending", finding.Resolution);
        Assert.Empty(finding.Evidence);
        Assert.Single(finding.ArchivedEvidence);
        Assert.Single(Assert.Single(data.Preparation!.Review!.Decisions).Evidence);
        Assert.Equal("Producto A / detalle", data.Details[0].Detail);
        Patch(data, new() { Key = data.Preparation.Rows[0].Key, Restore = true });
        var restored = Assert.Single(Case(data).Findings);
        Assert.Equal("pending", restored.Resolution);
        Assert.Equal(history.Id, Assert.Single(Assert.Single(restored.Evidence).Candidates).HistoryId);
        Assert.Equal("Producto A / detalle", data.Preparation.Rows[0].Details[0]);
    }

    [Fact]
    public void Restoring_an_edited_automatic_row_does_not_fabricate_a_new_pending_business_case()
    {
        var data = Prepare([Sale("1")]); var key = data.Preparation!.Rows[0].Key;
        Patch(data, new() { Key = key, Line = "Otra" });
        Assert.Equal(1, ReportReviewPolicy.Summary(data).PendingCases);
        Patch(data, new() { Key = key, Restore = true });
        Assert.Equal(0, ReportReviewPolicy.Summary(data).PendingCases);
        Assert.Equal("4500", data.Preparation.Rows[0].Op);
        Assert.Equal(1, ReportReviewPolicy.Summary(data).AutomaticRows);
        Assert.Equal(0, ReportReviewPolicy.Summary(data).HumanResolvedRows);
        Assert.Empty(data.Preparation.RowEdits);
        Assert.Contains(data.Preparation.Changes, c => c.Manual && c.Field == "LINEA");
    }

    [Fact]
    public void Restoring_an_original_shared_amount_observation_does_not_create_a_confirmation_decision()
    {
        var data = Prepare([Sale("1"), Sale("2", line: "DIGITAL")]);
        var key = data.Preparation!.Rows[0].Key;
        Patch(data, new() { Key = key, Amount = 100, SetAmount = true });
        var decisionCount = data.Preparation.Review!.Decisions.Count;
        Assert.Equal("resolved", Assert.Single(Case(data, key).Findings).Resolution);
        Patch(data, new() { Key = key, Restore = true });
        Assert.Equal(100, data.Preparation.Rows[0].Amount);
        Assert.Equal(decisionCount, data.Preparation.Review.Decisions.Count);
        Assert.Equal("pending", Assert.Single(Case(data, key).Findings).Resolution);
        Assert.Null(Assert.Single(Case(data, key).Findings).DecisionId);
        Assert.Equal(2, ReportReviewPolicy.Summary(data).ValidationRows);
    }

    [Fact]
    public void A_current_manual_field_edit_counts_as_human_but_FACTURA_alone_does_not()
    {
        var data = Prepare([Sale("1"), Sale("2", number: "8890")]);
        var a = data.Preparation!.Rows[0].Key; var b = data.Preparation.Rows[1].Key;
        Patch(data, new() { Key = a, Client = "Nombre ajustado" });
        Patch(data, new() { Key = b, Factura = "0001" });
        Assert.Equal(1, ReportReviewPolicy.Summary(data).HumanResolvedRows);
        Assert.Equal(1, ReportReviewPolicy.Summary(data).AutomaticRows);
        Patch(data, new() { Key = a, Restore = true });
        Assert.Equal(0, ReportReviewPolicy.Summary(data).HumanResolvedRows);
        Assert.Equal(2, ReportReviewPolicy.Summary(data).AutomaticRows);
    }

    [Fact]
    public void File_warnings_are_independent_and_cannot_be_hidden_by_resolving_all_row_cases()
    {
        var data = new SalesReportData { Details = [Sale("1")], Warnings = ["Advertencia de origen sin decisión definida"] };
        ReportPreparationEngine.Prepare(data, []);
        var summary = ReportReviewPolicy.Summary(data);
        Assert.Equal(1, summary.AutomaticRows);
        Assert.Equal(1, summary.FilePendingCases);
        Assert.Equal(1, summary.PendingCases);
        Assert.Throws<ReportValidationException>(() => ReportReviewPolicy.Approve(data.Preparation!, "auxiliar", 2, When));
    }

    [Fact]
    public void Previous_snapshots_and_incomplete_review_coverage_cannot_be_approved()
    {
        var data = Prepare([Sale("1")]);
        data.Preparation!.Review!.Cases.Clear();
        Assert.Equal(1, ReportReviewPolicy.Summary(data).PendingCases);
        Assert.Throws<ReportValidationException>(() => ReportReviewPolicy.Approve(data.Preparation, "auxiliar", 2, When));
        Assert.False(ReportReviewPolicy.IsApproved(data.Preparation));
        data.Preparation.RuleVersion = 2; data.Preparation.Review = null;
        Assert.Throws<ReportValidationException>(() => ReportReviewPolicy.Approve(data.Preparation, "auxiliar", 2, When));
        Assert.False(ReportReviewPolicy.IsApproved(data.Preparation));
        Assert.Null(JsonSerializer.Deserialize<PreparedReportData>("{\"RuleVersion\":2,\"Rows\":[]}")!.Review);
    }

    [Fact]
    public void A_V3_legacy_historical_patch_cannot_bypass_case_candidate_validation()
    {
        var historical = Historical("001"); var data = Prepare([Sale("1", op: "")], historical);
        Assert.Throws<ReportValidationException>(() => ReportPreparationEngine.Apply(data, [new() { Key = data.Preparation!.Rows[0].Key, HistoryIds = [historical.Id] }], "auxiliar"));
        Assert.Throws<ReportValidationException>(() => ReportPreparationEngine.Apply(data, [new() { Key = data.Preparation!.Rows[0].Key, HistoryReferenceId = historical.Id }], "auxiliar"));
        Assert.Equal("pending", Assert.Single(Case(data).Findings).Resolution);
    }
}
