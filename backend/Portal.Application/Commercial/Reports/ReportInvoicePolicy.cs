namespace Portal.Application.Commercial.Reports;

/// <summary>Completes the invoice default without preparing sales or rewriting human edits.</summary>
public static class ReportInvoicePolicy
{
    public static SalesReportData ApplyDefaults(SalesReportData data)
    {
        if (data.Preparation is not { } preparation) return data;

        for (var index = 0; index < preparation.AutomaticRows.Count; index++)
        {
            var row = preparation.AutomaticRows[index];
            if (string.IsNullOrEmpty(row.Factura))
                preparation.AutomaticRows[index] = row with { Factura = ReportPreparationEngine.DefaultFactura };
        }
        for (var index = 0; index < preparation.Rows.Count; index++)
        {
            var row = preparation.Rows[index];
            if (!string.IsNullOrEmpty(row.Factura) || HasInvoiceOverride(preparation, row.Key)) continue;
            preparation.Rows[index] = row with { Factura = ReportPreparationEngine.DefaultFactura };
        }
        return data;
    }

    private static bool HasInvoiceOverride(PreparedReportData preparation, string key)
    {
        // Null means the field was not edited. An empty string is an explicit
        // invoice edit and must survive reopening, saving and exporting.
        if (preparation.RowEdits.Any(edit => edit.Key == key && edit.Factura is not null)) return true;
        // Older snapshots may retain a change without an effective patch. A
        // later restore supersedes that intent rather than preserving it forever.
        var last = preparation.Changes.LastOrDefault(change => change.RowKey == key && change.Manual &&
            (change.Field == "FACTURA" || change.Kind == "manual_restore" && change.Field == "Fila"));
        return last is not null && last.Kind != "manual_restore";
    }
}
