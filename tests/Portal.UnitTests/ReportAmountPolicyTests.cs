using System.Text.Json;
using Portal.Application.Commercial.Reports;

namespace Portal.UnitTests;

public sealed class ReportAmountPolicyTests
{
    private static readonly DateTimeOffset When = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    private static SalesDetail Sale(string id, decimal amount = 100m, string op = "4500", string number = "8889") => new()
    {
        Id = id, Number = number, ManagerOp = op, Detail = "Producto A / detalle " + id,
        Line = "EMPAQUE", RawAmount = amount, Date = "2026-10-08", Client = "Cliente",
        SourceRow = int.Parse(id) + 1, SourceFile = "ventas.xlsx", Sheet = "Ventas"
    };

    private static OpRecordDto History(string number)
    {
        var cells = new string[21];
        cells[0] = number; cells[5] = "Cliente histórico"; cells[6] = "Producto A"; cells[9] = "Producto A";
        return new(Guid.NewGuid(), number, "", "Cliente histórico", "Producto A", null, When, "Histórico", new() { Cells = cells });
    }

    private static SalesReportData Prepared(params SalesDetail[] rows)
    {
        var data = new SalesReportData { Details = rows.ToList() };
        ReportPreparationEngine.Prepare(data, []);
        return data;
    }

    private static void Patch(SalesReportData data, PreparedRowEdit edit)
    {
        var previous = JsonSerializer.Deserialize<PreparedReportData>(JsonSerializer.Serialize(data.Preparation))!;
        ReportPreparationEngine.Apply(data, [edit], "auxiliar");
        ReportReviewPolicy.Reconcile(data, previous, "auxiliar", 2, When);
    }

    private static ReportReviewCommand CandidateDecision(SalesReportData data, params Guid[] ids)
    {
        var row = Assert.Single(data.Preparation!.Rows);
        var current = Assert.Single(data.Preparation.Review!.Cases, c => c.RowKey == row.Key);
        return new(current.Id, current.Findings.Where(f => f.Field == "NUMERO OP" && f.Resolution == "pending")
            .Select(f => f.Id).ToArray(), "select_candidates", ids, null);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(0)]
    public void A_single_OP_has_the_exact_automatic_Manager_value_and_editable_F01(decimal amount)
    {
        var data = Prepared(Sale("1", amount));
        var row = Assert.Single(data.Preparation!.Rows);
        Assert.Equal(1, row.OpCount);
        Assert.Equal("automatic", row.AmountMode);
        Assert.Equal(amount, row.Amount);
        Assert.Equal("F01", row.Factura);
        Patch(data, new() { Key = row.Key, Factura = "Texto libre" });
        Assert.Equal("Texto libre", data.Preparation.Rows[0].Factura);
        Assert.Equal(amount, data.Preparation.Rows[0].Amount);
        Assert.Equal(amount, data.Details[0].RawAmount);
    }

    [Theory]
    [InlineData(75)]
    [InlineData(null)]
    public void A_single_OP_rejects_an_arbitrary_manual_amount_without_applying_other_fields(int? input)
    {
        decimal? amount = input;
        var data = Prepared(Sale("1"));
        var before = JsonSerializer.Serialize(data);
        Assert.Throws<ReportValidationException>(() => ReportPreparationEngine.Apply(data,
            [new() { Key = data.Preparation!.Rows[0].Key, Factura = "No guardar", Amount = amount, SetAmount = true }], "auxiliar"));
        Assert.Equal(before, JsonSerializer.Serialize(data));
    }

    [Fact]
    public void Several_final_OPs_start_blank_and_keep_a_deliberate_manual_zero_through_unrelated_edits()
    {
        var data = Prepared(Sale("1"));
        var key = data.Preparation!.Rows[0].Key;
        Patch(data, new() { Key = key, Op = "4500 / 4501" });
        var row = data.Preparation.Rows[0];
        Assert.Equal(2, row.OpCount);
        Assert.Equal("manual", row.AmountMode);
        Assert.Null(row.Amount);
        Assert.True(ReportPreparationEngine.IncompleteTotals(data.Preparation));
        Patch(data, new() { Key = key, Amount = 0m, SetAmount = true });
        Patch(data, new() { Key = key, Op = "4500 / 4501", Factura = "Factura corregida", Client = "Cliente corregido" });
        row = data.Preparation.Rows[0];
        Assert.Equal(0m, row.Amount);
        Assert.Equal("manual", row.AmountMode);
        Assert.Equal(2, row.OpCount);
        Assert.Equal("Factura corregida", row.Factura);
        Assert.Equal("Cliente corregido", row.Client);
        Assert.Equal(100m, data.Details[0].RawAmount);
        Assert.Equal(100m, Assert.Single(data.Preparation.AutomaticRows).Amount);
    }

    [Fact]
    public void Returning_from_several_OPs_to_one_recovers_Manager_instead_of_the_saved_manual_total()
    {
        var data = Prepared(Sale("1", 125m));
        var key = data.Preparation!.Rows[0].Key;
        Patch(data, new() { Key = key, Op = "4500/4501" });
        Patch(data, new() { Key = key, Amount = 500m, SetAmount = true });
        Patch(data, new() { Key = key, Op = "4501" });
        Assert.Equal(125m, data.Preparation.Rows[0].Amount);
        Assert.Equal("automatic", data.Preparation.Rows[0].AmountMode);
        Assert.Equal(1, data.Preparation.Rows[0].OpCount);
        Assert.DoesNotContain(data.Preparation.Rows[0].Issues, issue => issue.Contains("sin determinar"));
        Assert.Equal(125m, data.Details[0].RawAmount);
        Assert.Throws<ReportValidationException>(() => ReportPreparationEngine.Apply(data,
            [new() { Key = key, Amount = 500m, SetAmount = true }], "auxiliar"));
    }

    [Fact]
    public void Reordering_or_spacing_the_same_final_OP_set_keeps_the_manual_total_and_its_saved_patch()
    {
        var data = Prepared(Sale("1"));
        var key = data.Preparation!.Rows[0].Key;
        Patch(data, new() { Key = key, Op = "4500/4501" });
        Patch(data, new() { Key = key, Amount = 250m, SetAmount = true });
        Patch(data, new() { Key = key, Op = " 4501 / 4500 / 4501 ", Factura = "Libre" });
        Assert.Equal(2, data.Preparation.Rows[0].OpCount);
        Assert.Equal(250m, data.Preparation.Rows[0].Amount);
        Assert.Equal("manual", data.Preparation.Rows[0].AmountMode);
        var saved = Assert.Single(data.Preparation.RowEdits);
        Assert.True(saved.SetAmount);
        Assert.Equal(250m, saved.Amount);
        Assert.Equal("Libre", data.Preparation.Rows[0].Factura);
    }

    [Fact]
    public void A_single_OP_can_confirm_an_existing_shared_amount_observation_without_changing_Manager_value()
    {
        var first = Sale("1");
        var second = Sale("2"); second.Line = "OTRA LINEA";
        var data = Prepared(first, second);
        var row = data.Preparation!.Rows[0];
        Assert.Equal("automatic", row.AmountMode);
        Assert.Contains(data.Preparation.Review!.Cases.SelectMany(c => c.Findings),
            f => f.Field == "VALOR_BRUT" && f.Code == "shared_amount_observation" && f.Resolution == "pending");
        Patch(data, new() { Key = row.Key, Amount = row.Amount, SetAmount = true });
        var current = Assert.Single(data.Preparation.Review.Cases, c => c.RowKey == row.Key);
        Assert.Equal("resolved", Assert.Single(current.Findings, f => f.Code == "shared_amount_observation").Resolution);
        Assert.Equal(100m, data.Preparation.Rows[0].Amount);
        Assert.Equal("automatic", data.Preparation.Rows[0].AmountMode);
        Assert.Equal(100m, first.RawAmount);
        Assert.Equal(100m, second.RawAmount);
    }

    [Fact]
    public void A_shared_amount_confirmation_reopened_by_an_OP_change_can_be_confirmed_again_without_a_fake_override()
    {
        var first = Sale("1");
        var second = Sale("2"); second.Line = "OTRA LINEA";
        var data = Prepared(first, second);
        var firstKey = data.Preparation!.Rows[0].Key;
        var secondKey = data.Preparation.Rows[1].Key;
        Patch(data, new() { Key = firstKey, Amount = 100m, SetAmount = true });
        Patch(data, new() { Key = secondKey, Amount = 100m, SetAmount = true });
        Assert.False(ReportPreparationEngine.IncompleteTotals(data.Preparation));
        var current = Assert.Single(data.Preparation.Review!.Cases, c => c.RowKey == firstKey);
        var shared = Assert.Single(current.Findings, f => f.Code == "shared_amount_observation");
        Assert.Equal("resolved", shared.Resolution);
        Assert.DoesNotContain(shared.Reason, data.Preparation.Rows[0].Issues);

        Patch(data, new() { Key = firstKey, Op = "4501" });
        current = Assert.Single(data.Preparation.Review!.Cases, c => c.RowKey == firstKey);
        shared = Assert.Single(current.Findings, f => f.Code == "shared_amount_observation");
        Assert.Equal("pending", shared.Resolution);
        Assert.Equal("automatic", data.Preparation.Rows[0].AmountMode);
        Assert.Equal(100m, data.Preparation.Rows[0].Amount);
        Assert.Contains(shared.Reason, data.Preparation.Rows[0].Issues);
        Assert.True(ReportPreparationEngine.IncompleteTotals(data.Preparation));
        var editsBefore = data.Preparation.Changes.Count(c => c.Manual && c.Field == "VALOR_BRUT");

        Patch(data, new() { Key = firstKey, Amount = 100m, SetAmount = true });
        current = Assert.Single(data.Preparation.Review!.Cases, c => c.RowKey == firstKey);
        shared = Assert.Single(current.Findings, f => f.Code == "shared_amount_observation");
        Assert.Equal("resolved", shared.Resolution);
        Assert.DoesNotContain(shared.Reason, data.Preparation.Rows[0].Issues);
        Assert.Equal(editsBefore + 1, data.Preparation.Changes.Count(c => c.Manual && c.Field == "VALOR_BRUT"));
        Assert.Equal(100m, data.Preparation.Rows[0].Amount);
        Assert.Equal("automatic", data.Preparation.Rows[0].AmountMode);
        Assert.False(ReportPreparationEngine.IncompleteTotals(data.Preparation));
        Assert.Equal(100m, first.RawAmount);
        Assert.Equal(100m, second.RawAmount);
    }

    [Fact]
    public void Several_historical_records_with_the_same_OP_number_are_one_automatic_OP_even_with_repeated_details()
    {
        var first = History("4500"); var duplicate = History(" 4500 ");
        var data = new SalesReportData { Details = [Sale("1"), Sale("2"), Sale("3")] };
        ReportPreparationEngine.Prepare(data, [first, duplicate]);
        ReportReviewPolicy.ApplyDecisions(data, [CandidateDecision(data, first.Id, duplicate.Id)], "auxiliar", 2, When);
        var row = Assert.Single(data.Preparation!.Rows);
        Assert.Equal("4500", row.Op);
        Assert.Equal(1, row.OpCount);
        Assert.Equal("automatic", row.AmountMode);
        Assert.Equal(100m, row.Amount); // Equal Manager values are kept once, not multiplied by source or history count.
        Assert.Equal(3, row.Details.Length);
        Assert.Equal(2, Assert.Single(data.Preparation.Review!.Decisions).HistoryIds.Length);
    }

    [Fact]
    public void A_single_confirmed_OP_with_different_Manager_amounts_stays_validation_and_rejects_manual_fill()
    {
        var history = History("4500");
        var data = new SalesReportData { Details = [Sale("1", 100m), Sale("2", 25m)] };
        ReportPreparationEngine.Prepare(data, [history]);
        ReportReviewPolicy.ApplyDecisions(data, [CandidateDecision(data, history.Id)], "auxiliar", 2, When);
        var row = Assert.Single(data.Preparation!.Rows);
        Assert.Equal(1, row.OpCount);
        Assert.Equal("validation", row.AmountMode);
        Assert.Null(row.Amount);
        Assert.Contains(data.Preparation.Review!.Cases.SelectMany(c => c.Findings),
            f => f.Field == "VALOR_BRUT" && f.Resolution == "pending");
        var before = JsonSerializer.Serialize(data);
        Assert.Throws<ReportValidationException>(() => ReportPreparationEngine.Apply(data,
            [new() { Key = row.Key, Amount = 125m, SetAmount = true }], "auxiliar"));
        Assert.Equal(before, JsonSerializer.Serialize(data));
        Assert.Equal(new[] { 100m, 25m }, data.Details.Select(d => d.RawAmount));
    }

    [Fact]
    public void Going_back_to_one_OP_with_heterogeneous_source_amounts_does_not_keep_or_derive_the_manual_total()
    {
        var data = Prepared(Sale("1", 100m), Sale("2", 25m));
        var key = data.Preparation!.Rows[0].Key;
        Patch(data, new() { Key = key, Op = "4500/4501" });
        Patch(data, new() { Key = key, Amount = 999m, SetAmount = true });
        Patch(data, new() { Key = key, Op = "4500" });
        Assert.Null(data.Preparation.Rows[0].Amount);
        Assert.Equal("validation", data.Preparation.Rows[0].AmountMode);
        Assert.Equal(1, data.Preparation.Rows[0].OpCount);
        Assert.True(ReportPreparationEngine.IncompleteTotals(data.Preparation));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void Saved_legacy_rule_versions_remain_editable_without_automatic_reclassification(int version)
    {
        var data = Prepared(Sale("1"));
        data.Preparation!.RuleVersion = version;
        var key = data.Preparation.Rows[0].Key;
        Patch(data, new() { Key = key, Amount = 77m, SetAmount = true });
        Assert.Equal(77m, data.Preparation.Rows[0].Amount);
        Assert.Equal(version, data.Preparation.RuleVersion);
        Assert.Equal(100m, data.Details[0].RawAmount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("NA")]
    [InlineData("N/A")]
    public void Missing_OP_is_not_split_into_multiple_OPs_or_assigned_from_the_history(string op)
    {
        var history = History("4500");
        var data = new SalesReportData { Details = [Sale("1", op: op)] };
        ReportPreparationEngine.Prepare(data, [history]);
        var row = Assert.Single(data.Preparation!.Rows);
        Assert.Equal("N/A", row.Op);
        Assert.Equal(0, row.OpCount);
        Assert.Equal(100m, row.Amount);
        Assert.Contains(data.Preparation.Review!.Cases.SelectMany(c => c.Findings),
            f => f.Field == "NUMERO OP" && f.Resolution == "pending");
        Assert.Empty(data.Preparation.Review.Decisions);
    }

    [Fact]
    public void Restoring_a_row_restores_the_original_automatic_amount_and_invoice_after_manual_multiple_OP_edit()
    {
        var data = Prepared(Sale("1", 145m));
        var key = data.Preparation!.Rows[0].Key;
        Patch(data, new() { Key = key, Op = "4500/4501", Factura = "Otro texto" });
        Patch(data, new() { Key = key, Amount = 888m, SetAmount = true });
        Patch(data, new() { Key = key, Restore = true });
        var row = Assert.Single(data.Preparation.Rows);
        Assert.Equal("4500", row.Op);
        Assert.Equal(1, row.OpCount);
        Assert.Equal("automatic", row.AmountMode);
        Assert.Equal(145m, row.Amount);
        Assert.Equal("F01", row.Factura);
        Assert.Empty(data.Preparation.RowEdits);
    }

    [Fact]
    public void Restoring_a_candidate_case_to_its_original_NA_does_not_leave_a_phantom_manual_amount_finding()
    {
        var first = History("4500"); var second = History("4501");
        var data = new SalesReportData { Details = [Sale("1"), Sale("2")] };
        ReportPreparationEngine.Prepare(data, [first, second]);
        var key = data.Preparation!.Rows[0].Key;
        ReportReviewPolicy.ApplyDecisions(data, [CandidateDecision(data, first.Id, second.Id)], "auxiliar", 2, When);
        Assert.Equal("manual", data.Preparation.Rows[0].AmountMode);
        Assert.Null(data.Preparation.Rows[0].Amount);
        Assert.Contains(data.Preparation.Review!.Cases.SelectMany(c => c.Findings), f => f.Code == "manual_amount_required");
        Patch(data, new() { Key = key, Amount = 200m, SetAmount = true });
        Patch(data, new() { Key = key, Restore = true });
        var row = Assert.Single(data.Preparation.Rows);
        Assert.Equal("N/A", row.Op);
        Assert.Equal(0, row.OpCount);
        Assert.Equal("legacy", row.AmountMode);
        Assert.Equal(100m, row.Amount);
        Assert.Empty(data.Preparation.RowEdits);
        Assert.DoesNotContain(data.Preparation.Review.Cases.SelectMany(c => c.Findings), f => f.Code == "manual_amount_required");
        Assert.Contains(data.Preparation.Review.Cases.SelectMany(c => c.Findings),
            f => f.Field == "NUMERO OP" && f.Resolution == "pending");
        Assert.False(ReportPreparationEngine.IncompleteTotals(data.Preparation));
        Assert.All(data.Details, d => Assert.Equal(100m, d.RawAmount));
    }

    [Fact]
    public void Resolving_a_missing_OP_after_clearing_its_legacy_amount_restores_Manager_and_resolves_the_stale_amount_finding()
    {
        var history = History("4500");
        var data = new SalesReportData { Details = [Sale("1", op: "")] };
        ReportPreparationEngine.Prepare(data, [history]);
        var sourceBefore = JsonSerializer.Serialize(data.Details);
        var key = data.Preparation!.Rows[0].Key;
        Assert.Equal("N/A", data.Preparation.Rows[0].Op);
        Assert.Equal("legacy", data.Preparation.Rows[0].AmountMode);
        Patch(data, new() { Key = key, Amount = null, SetAmount = true });
        Assert.Null(data.Preparation.Rows[0].Amount);
        Assert.Contains(data.Preparation.Review!.Cases.SelectMany(c => c.Findings),
            f => f.Code == "amount_undetermined" && f.Resolution == "pending");

        ReportReviewPolicy.ApplyDecisions(data, [CandidateDecision(data, history.Id)], "auxiliar", 3, When);
        var row = Assert.Single(data.Preparation.Rows);
        Assert.Equal("4500", row.Op);
        Assert.Equal(1, row.OpCount);
        Assert.Equal("automatic", row.AmountMode);
        Assert.Equal(100m, row.Amount);
        Assert.DoesNotContain(row.Issues, issue => issue.Contains("sin determinar"));
        var amountFinding = Assert.Single(data.Preparation.Review.Cases.SelectMany(c => c.Findings),
            f => f.Code == "amount_undetermined");
        Assert.Equal("resolved", amountFinding.Resolution);
        Assert.Equal("automatic", amountFinding.Provenance);
        Assert.Equal(0, ReportReviewPolicy.Summary(data).PendingCases);
        Assert.False(ReportPreparationEngine.IncompleteTotals(data.Preparation));
        Assert.Equal(sourceBefore, JsonSerializer.Serialize(data.Details));
    }
}
