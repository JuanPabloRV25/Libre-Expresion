using Portal.Application.Commercial.Reports;
namespace Portal.UnitTests;

public class SalesReportEngineTests
{
    private static SalesDetail Detail(string id, string document, Guid op, string product = "Etiquetas", string line = "EMPAQUE") => new()
    { Id = id, MatchKey = id, DocumentId = document, Number = document, SelectedOpId = op, SelectedOpNumber = "25280",
        SelectedProduct = product, Detail = "Detalle " + id, Line = line, Reviewed = true, RawAmount = 823500 };

    [Fact]
    public void Repeated_amounts_are_not_summed_and_every_detail_survives()
    {
        var op = Guid.NewGuid(); var data = new SalesReportData
        { Details = [Detail("1", "8877", op), Detail("2", "8877", op), Detail("3", "8877", op)],
            Documents = [new() { Id = "8877", Number = "8877", SourceAmount = 823500, ConfirmedAmount = 823500 }] };
        var group = Assert.Single(SalesReportEngine.Groups(data));
        Assert.Equal(new[] { "Detalle 1", "Detalle 2", "Detalle 3" }, group.Details);
        Assert.Equal(823500, group.Amount); Assert.Equal("F01", group.Factura);
        Assert.True(SalesReportEngine.CanExport(data, [group], SalesReportEngine.Controls(data, [group])));
        data.Edits.Add(new() { Key = group.Key, Factura = "Manual", Amount = 823500 });
        var complete = SalesReportEngine.Groups(data); var control = Assert.Single(SalesReportEngine.Controls(data, complete));
        Assert.Equal(823500, control.Distributed); Assert.Equal(0, control.Difference);
        Assert.True(SalesReportEngine.CanExport(data, complete, [control]));
    }

    [Fact]
    public void Different_numbers_products_and_lines_are_kept_separate()
    {
        var op = Guid.NewGuid(); var data = new SalesReportData { Details = [Detail("1", "8876", op),
            Detail("2", "8877", op), Detail("3", "8876", op, "Separador"), Detail("4", "8876", op, line: "PUBLICOMERCIAL")] };
        Assert.Equal(4, SalesReportEngine.Groups(data).Length);
        data.Details[0].ManualGroup = "juntos"; data.Details[2].ManualGroup = "juntos";
        Assert.Contains(SalesReportEngine.Groups(data), g => g.Issues.Any(i => i.Contains("productos distintos")));
    }

    [Fact]
    public void A_blank_confirmed_total_is_not_zero_and_differences_block_export()
    {
        var data = new SalesReportData { Details = [Detail("1", "8877", Guid.NewGuid())],
            Documents = [new() { Id = "8877", Number = "8877", SourceAmount = 0 }] };
        var group = SalesReportEngine.Groups(data)[0]; data.Edits.Add(new() { Key = group.Key, Amount = 0, Factura = "FE01" });
        var groups = SalesReportEngine.Groups(data); var controls = SalesReportEngine.Controls(data, groups);
        Assert.Null(controls[0].Difference); Assert.False(SalesReportEngine.CanExport(data, groups, controls));
        data.Documents[0].ConfirmedAmount = 0;
        Assert.True(SalesReportEngine.CanExport(data, groups, SalesReportEngine.Controls(data, groups)));
        data.Documents[0].ConfirmedAmount = 1;
        Assert.False(SalesReportEngine.CanExport(data, groups, SalesReportEngine.Controls(data, groups)));
    }

    [Fact]
    public void Updating_a_source_retains_exact_decisions_and_archives_affected_adjustments()
    {
        var op = Guid.NewGuid(); var original = Detail("1", "8877", op); original.Reason = "Verificado";
        var old = new SalesReportData { Details = [original], Documents = [new() { Id = "8877", SourceAmount = 823500, ConfirmedAmount = 823500 }] };
        var key = SalesReportEngine.GroupKey(original); old.Edits.Add(new() { Key = key, Amount = 823500, Factura = "Manual" });
        var unchanged = Detail("new-position", "8877", op); unchanged.MatchKey = "1";
        var incoming = new SalesReportData { Details = [unchanged], Documents = [new() { Id = "8877", SourceAmount = 823500 }] };
        SalesReportEngine.ReplaceSource(old, incoming, out var retained, out var added, out var removed);
        Assert.Equal((1, 0, 0), (retained, added, removed)); Assert.Equal("1", incoming.Details[0].Id);
        Assert.Equal("Verificado", incoming.Details[0].Reason); Assert.Single(incoming.Edits); Assert.Equal(823500, incoming.Documents[0].ConfirmedAmount);
        incoming.Details.Add(Detail("new", "8877", op));
        SalesReportEngine.ReplaceSource(old, incoming, out _, out _, out _);
        Assert.Empty(incoming.Edits); Assert.Single(incoming.UnappliedEdits); Assert.Null(incoming.Documents[0].ConfirmedAmount);
    }
}
