using System.Globalization;
using System.Text.RegularExpressions;

namespace Portal.Application.Commercial.Reports;

/// <summary>Confirmed amount rules over final OP numbers; never assigns or consolidates OP.</summary>
public static class ReportAmountPolicy
{
    public static string[] Numbers(string? op)
    {
        if (ReportPreparationEngine.IsUnspecifiedOp(op)) return [];
        // Final OP selections already use '/'. Preserve N/A as one unspecified token.
        return Regex.Matches(op ?? "", @"\bN\s*/\s*A\b|[^/]+", RegexOptions.IgnoreCase)
            .Select(m => m.Value).Where(v => !ReportPreparationEngine.IsUnspecifiedOp(v))
            .Select(ReportPreparationEngine.Identity).Distinct(StringComparer.Ordinal).ToArray();
    }

    public static string OpSet(string? op) => string.Join("\n", Numbers(op).Order(StringComparer.Ordinal));

    public static SalesGroup Synchronize(SalesReportData data, PreparedReportData preparation, SalesGroup row, SalesGroup? previous = null)
    {
        if (preparation.RuleVersion < 4) return row;
        var count = Numbers(row.Op).Length;
        var amounts = data.Details.Where(d => row.DetailIds.Contains(d.Id)).Select(d => d.RawAmount).Distinct().ToArray();
        decimal? sourceAmount = amounts.Length == 1 ? amounts[0] : null;
        var mode = count > 1 ? "manual" : count == 1 ? sourceAmount.HasValue ? "automatic" : "validation" : "legacy";
        var amount = mode switch
        {
            "automatic" => sourceAmount,
            "validation" => null,
            "manual" when previous?.AmountMode == "manual" && OpSet(previous.Op) == OpSet(row.Op) => row.Amount,
            "manual" => null,
            _ => row.Amount
        };
        var updated = row with { Amount = amount, AmountMode = mode, OpCount = count };
        if (previous is not null && (previous.AmountMode != mode || OpSet(previous.Op) != OpSet(row.Op)))
        {
            // A previous manual total belongs to its OP set, not to a newly selected set.
            var saved = preparation.RowEdits.SingleOrDefault(e => e.Key == row.Key);
            if (saved is not null) { saved.SetAmount = false; saved.Amount = null; }
        }
        if (row.Amount != amount)
        {
            var reason = mode switch
            {
                "manual" => "Hay varias OP finales. La auxiliar debe escribir el total; el importe inicial queda vacío.",
                "automatic" => "Hay una sola OP. Se toma automáticamente el importe inequívoco del informe de ventas.",
                _ => "Hay una sola OP con importes de origen distintos. Se conserva pendiente de validación sin sumar ni escoger un importe."
            };
            preparation.Changes.Add(new ReportChange
            {
                Id = SalesReportEngine.Key("amount-rule-v4", row.Key, preparation.Changes.Count.ToString(CultureInfo.InvariantCulture)),
                RowKey = row.Key, Kind = "amount_rule_applied", Field = "VALOR_BRUT",
                Before = [row.Amount?.ToString(CultureInfo.InvariantCulture) ?? ""], After = amount?.ToString(CultureInfo.InvariantCulture) ?? "",
                Reason = reason, DetailIds = row.DetailIds.ToArray()
            });
        }
        return updated;
    }

    public static void ValidateEdit(SalesGroup row, PreparedRowEdit edit)
    {
        if (!edit.SetAmount) return;
        if (row.AmountMode == "validation")
            throw new ReportValidationException("Esta única OP tiene importes de Manager distintos y permanece pendiente de validación; no se puede escoger un importe manualmente.");
        if (row.AmountMode == "automatic" && row.Amount != edit.Amount)
            throw new ReportValidationException("VALOR_BRUT es automático cuando hay una sola OP. El importe se toma del informe de ventas.");
    }
}
