using System.Text.Json;
using Portal.Application.Commercial.Reports;

namespace Portal.UnitTests;

public class ReportInvoicePolicyTests
{
    private static SalesReportData SavedBlankInvoice(int version = 3)
    {
        var data = new SalesReportData
        {
            SourceFile = "Ventas.xlsx", Sha256 = "original-source", CurrentSourceId = Guid.NewGuid(),
            Details = [new SalesDetail { Id = "source-1", Number = "9100", ManagerOp = "4500", Line = "Línea A",
                Detail = "Detalle original", RawAmount = 100, Client = "Cliente", Date = "2026-10-07" }]
        };
        var prepared = ReportPreparationEngine.Prepare(data, []);
        prepared.RuleVersion = version;
        prepared.Rows[0] = prepared.Rows[0] with { Factura = "" };
        prepared.AutomaticRows[0] = prepared.AutomaticRows[0] with { Factura = "" };
        return data;
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Invoice_default_changes_only_invoice_cells_and_is_idempotent(int version)
    {
        var data = SavedBlankInvoice(version);
        var preparation = data.Preparation!;
        var original = preparation.Rows[0];
        preparation.RowEdits.Add(new() { Key = original.Key, Client = "Cliente" });
        var before = JsonSerializer.Serialize(data);

        Assert.Same(data, ReportInvoicePolicy.ApplyDefaults(data));
        Assert.Equal("F01", preparation.Rows[0].Factura);
        Assert.Equal("F01", preparation.AutomaticRows[0].Factura);
        Assert.Equal(version, preparation.RuleVersion);
        Assert.Equal(JsonSerializer.Serialize(original), JsonSerializer.Serialize(preparation.Rows[0] with { Factura = "" }));
        var projected = JsonSerializer.Serialize(data);
        ReportInvoicePolicy.ApplyDefaults(data);
        Assert.Equal(projected, JsonSerializer.Serialize(data));
        preparation.Rows[0] = preparation.Rows[0] with { Factura = "" };
        preparation.AutomaticRows[0] = preparation.AutomaticRows[0] with { Factura = "" };
        Assert.Equal(before, JsonSerializer.Serialize(data));
    }

    [Theory]
    [InlineData("")]
    [InlineData("00017")]
    [InlineData("  Texto libre  ")]
    public void Explicit_invoice_text_and_blank_survive_default_and_other_field_edits(string invoice)
    {
        var data = SavedBlankInvoice();
        var preparation = data.Preparation!;
        var key = preparation.Rows[0].Key;
        preparation.Rows[0] = preparation.Rows[0] with { Factura = invoice };
        preparation.RowEdits = [new() { Key = key, Factura = invoice }];
        ReportInvoicePolicy.ApplyDefaults(data);
        Assert.Equal(invoice, preparation.Rows[0].Factura);
        Assert.Equal("F01", preparation.AutomaticRows[0].Factura);
        ReportPreparationEngine.Apply(data, [new() { Key = key, Client = "Nombre corregido" }], "auxiliar");
        ReportInvoicePolicy.ApplyDefaults(data);
        Assert.Equal(invoice, data.Preparation!.Rows[0].Factura);
        ReportPreparationEngine.Apply(data, [new() { Key = key, Restore = true }], "auxiliar");
        ReportInvoicePolicy.ApplyDefaults(data);
        Assert.Equal("F01", data.Preparation!.Rows[0].Factura);
        Assert.Empty(data.Preparation.RowEdits);
    }

    [Theory]
    [InlineData(false, "")]
    [InlineData(true, "F01")]
    public void Latest_invoice_change_preserves_blank_intent_unless_a_restore_supersedes_it(bool restored, string expected)
    {
        var data = SavedBlankInvoice();
        var preparation = data.Preparation!;
        var key = preparation.Rows[0].Key;
        preparation.Changes.Add(new() { RowKey = key, Field = "FACTURA", Kind = "manual_edit", Manual = true, After = "" });
        if (restored) preparation.Changes.Add(new() { RowKey = key, Field = "FACTURA", Kind = "manual_restore", Manual = true, After = "" });
        ReportInvoicePolicy.ApplyDefaults(data);
        Assert.Equal(expected, preparation.Rows[0].Factura);
        Assert.Empty(preparation.RowEdits);
    }

    [Fact]
    public void A_legacy_full_row_restore_retires_prior_blank_invoice_intent_even_without_an_invoice_delta()
    {
        var data = SavedBlankInvoice();
        var preparation = data.Preparation!;
        var key = preparation.Rows[0].Key;
        // The old default and the human blank are both empty. Restoring that
        // proposal removes the patch but has no FACTURA delta to record.
        preparation.RowEdits = [new() { Key = key, Factura = "" }];
        preparation.Changes.Add(new() { RowKey = key, Field = "FACTURA", Kind = "manual_edit",
            Manual = true, Before = ["FE01"], After = "" });
        var restored = ReportPreparationEngine.Apply(data, [new() { Key = key, Restore = true }], "auxiliar");
        Assert.Empty(restored.RowEdits);
        var change = restored.Changes.Last();
        Assert.Equal("manual_restore", change.Kind);
        Assert.Equal("Fila", change.Field);
        Assert.Equal("", restored.Rows[0].Factura);
        // Reopen the persisted shape before projecting the new invoice default.
        var reopened = JsonSerializer.Deserialize<SalesReportData>(JsonSerializer.Serialize(data))!;
        ReportInvoicePolicy.ApplyDefaults(reopened);
        Assert.Equal("F01", reopened.Preparation!.Rows[0].Factura);
        Assert.Equal("F01", reopened.Preparation.AutomaticRows[0].Factura);
        Assert.Empty(reopened.Preparation.RowEdits);
        Assert.Equal(JsonSerializer.Serialize(data.Details), JsonSerializer.Serialize(reopened.Details));
    }

    [Fact]
    public void Completing_default_does_not_transfer_an_old_content_approval()
    {
        var data = SavedBlankInvoice();
        var preparation = data.Preparation!;
        ReportReviewPolicy.Approve(preparation, "auxiliar", 7, DateTimeOffset.UtcNow);
        Assert.True(ReportReviewPolicy.IsApproved(preparation, 7));
        var originalApproval = JsonSerializer.Serialize(preparation.Review!.Approvals);
        ReportInvoicePolicy.ApplyDefaults(data);
        Assert.False(ReportReviewPolicy.IsApproved(preparation, 7));
        Assert.Equal(originalApproval, JsonSerializer.Serialize(preparation.Review.Approvals));
        Assert.Equal(0, ReportReviewPolicy.Summary(data).PendingCases);
    }

    [Theory]
    [InlineData(null, "F01")]
    [InlineData("", "")]
    [InlineData("00017", "00017")]
    public void Legacy_group_default_applies_only_without_an_existing_invoice_edit(string? invoice, string expected)
    {
        var data = new SalesReportData { Details = [new() { Id = "legacy-1", DocumentId = "sale", Detail = "Original", Line = "Línea" }] };
        if (invoice is not null) data.Edits.Add(new() { Key = SalesReportEngine.GroupKey(data.Details[0]), Factura = invoice });
        var before = JsonSerializer.Serialize(data);
        ReportInvoicePolicy.ApplyDefaults(data);
        var row = Assert.Single(SalesReportEngine.Groups(data));
        Assert.Equal(expected, row.Factura);
        Assert.Equal(invoice == "", row.Issues.Contains("Completa FACTURA."));
        Assert.Equal(before, JsonSerializer.Serialize(data));
    }
}
