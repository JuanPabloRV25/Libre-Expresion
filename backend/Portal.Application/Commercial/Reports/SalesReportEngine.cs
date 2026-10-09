using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Portal.Application.Commercial.Reports;

public static class SalesReportEngine
{
    public static string Normalize(string? value) => string.Concat((value ?? "").Normalize(NormalizationForm.FormD)
        .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(c)))
        .ToUpperInvariant();
    public static string Key(params string[] parts) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(string.Join("\u001f", parts)))).ToLowerInvariant();

    public static string GroupKey(SalesDetail detail) => Key(detail.DocumentId,
        detail.ManualGroup is { Length: > 0 } ? "manual:" + detail.ManualGroup.Trim() :
        detail.SelectedOpId.HasValue ? $"{detail.SelectedOpId}:{Normalize(detail.SelectedProduct)}:{Normalize(detail.Line)}" :
        "pending:" + detail.Id);

    public static SalesGroup[] Groups(SalesReportData data)
    {
        return data.Details.Where(d => !d.Excluded).GroupBy(GroupKey).Select(bucket =>
        {
            var details = bucket.ToArray(); var first = details[0];
            var edit = data.Edits.SingleOrDefault(x => x.Key == bucket.Key);
            var sourceDocument = data.Documents.FirstOrDefault(d => d.Id == first.DocumentId);
            var fullDocument = data.Details.Where(d => d.DocumentId == first.DocumentId).ToArray();
            var proposedAmount = details.Length == fullDocument.Length && details.All(d => d.SelectedOpId.HasValue && d.Reviewed)
                ? sourceDocument?.SourceAmount : null;
            var amount = edit is null ? proposedAmount : edit.Amount;
            var factura = edit?.Factura ?? ReportPreparationEngine.DefaultFactura;
            var issues = new List<string>();
            if (details.Any(x => !x.SelectedOpId.HasValue || !x.Reviewed)) issues.Add("Confirma la OP y el producto de cada detalle.");
            if (details.Any(x => string.IsNullOrWhiteSpace(x.SelectedProduct))) issues.Add("Completa el producto que falta en el registro histórico.");
            if (details.Select(x => Normalize(x.Line)).Distinct().Count() > 1) issues.Add("El grupo tiene líneas distintas. Revisa la agrupación.");
            if (details.Select(x => Normalize(x.SelectedProduct)).Where(x => x.Length > 0).Distinct().Count() > 1) issues.Add("Los productos distintos deben quedar en registros separados.");
            if (string.IsNullOrWhiteSpace(factura)) issues.Add("Completa FACTURA.");
            if (!amount.HasValue) issues.Add("Completa el importe de este registro.");
            if (string.Join("\n", details.Select(d => d.Detail)).Length > 32767) issues.Add("Los detalles superan el espacio de una celda de Excel. Divide el grupo.");
            var modified = edit is not null && (edit.Amount != proposedAmount || edit.Factura.Length > 0 || edit.Client is not null && edit.Client != first.Client || edit.Line is not null && edit.Line != first.Line || edit.Seller is not null && edit.Seller != first.Seller || edit.Reason.Length > 0)
                || details.Any(d => d.Reviewed && d.SelectedOpId.HasValue && d.ManagerOp != d.SelectedOpNumber || d.ManualGroup is not null);
            return new SalesGroup(bucket.Key, first.DocumentId, first.Number, first.Date,
                string.Join("/", details.Select(d => d.SelectedOpNumber).Where(x => x.Length > 0).Distinct()),
                string.Join(" / ", details.Select(d => d.SelectedProduct).Where(x => x.Length > 0).Distinct()),
                edit?.Client ?? first.Client, first.Term, amount, factura, edit?.Line ?? first.Line,
                edit?.Seller ?? first.Seller, details.Select(d => d.Detail).ToArray(), details.Select(d => d.Id).ToArray(),
                issues.ToArray(), modified);
        }).ToArray();
    }

    public static DocumentControl[] Controls(SalesReportData data, SalesGroup[] groups) =>
        data.Documents.Where(doc => data.Details.Any(d => d.DocumentId == doc.Id && !d.Excluded))
        .Select(doc =>
        {
            var distributed = groups.Where(g => g.DocumentId == doc.Id).Sum(g => g.Amount ?? 0);
            return new DocumentControl(doc.Id, doc.Number, doc.Client, doc.ConfirmedAmount,
                distributed, doc.ConfirmedAmount.HasValue ? doc.ConfirmedAmount - distributed : null);
        }).ToArray();

    public static bool CanExport(SalesReportData data, SalesGroup[] groups, DocumentControl[] controls) =>
        groups.Length > 0 && groups.All(g => g.Issues.Length == 0) &&
        controls.All(c => c.Expected.HasValue && c.Difference == 0) &&
        data.Details.Where(d => d.Excluded).All(d => !string.IsNullOrWhiteSpace(d.Reason));

    public static SalesReportData ReplaceSource(SalesReportData oldData, SalesReportData incoming,
        out int retained, out int added, out int removed)
    {
        var queues = oldData.Details.GroupBy(d => d.MatchKey).ToDictionary(g => g.Key, g => new Queue<SalesDetail>(g));
        retained = 0;
        foreach (var item in incoming.Details)
        {
            if (!queues.TryGetValue(item.MatchKey, out var queue) || !queue.TryDequeue(out var old)) continue;
            retained++;
            item.Id = old.Id; item.SelectedOpId = old.SelectedOpId; item.SelectedOpNumber = old.SelectedOpNumber;
            item.SelectedProduct = old.SelectedProduct; item.Reviewed = old.Reviewed; item.Excluded = old.Excluded;
            item.UsePortalCode = old.UsePortalCode;
            item.Reason = old.Reason; item.ManualGroup = old.ManualGroup;
        }
        added = incoming.Details.Count - retained; removed = oldData.Details.Count - retained;
        var validGroups = incoming.Details.Select(GroupKey).ToHashSet();
        incoming.Edits = oldData.Edits.Where(e => validGroups.Contains(e.Key)).ToList();
        incoming.UnappliedEdits = oldData.UnappliedEdits.Concat(oldData.Edits.Where(e => !validGroups.Contains(e.Key))).ToList();
        // A changed group's membership invalidates its amount/type allocation, even if its key survived.
        var changedEdits = incoming.Edits.Where(edit => !oldData.Details.Where(d => !d.Excluded && GroupKey(d) == edit.Key).Select(d => d.Id).Order()
            .SequenceEqual(incoming.Details.Where(d => !d.Excluded && GroupKey(d) == edit.Key).Select(d => d.Id).Order())).ToArray();
        incoming.UnappliedEdits.AddRange(changedEdits);
        incoming.Edits.RemoveAll(edit => changedEdits.Contains(edit));
        foreach (var doc in incoming.Documents)
        {
            doc.ConfirmedAmount = null; doc.Reason = "";
            var old = oldData.Documents.FirstOrDefault(d => d.Id == doc.Id);
            if (old is null || old.SourceAmount != doc.SourceAmount ||
                !oldData.Details.Where(d => d.DocumentId == doc.Id).Select(d => d.Id).Order()
                    .SequenceEqual(incoming.Details.Where(d => d.DocumentId == doc.Id).Select(d => d.Id).Order())) continue;
            doc.ConfirmedAmount = old.ConfirmedAmount; doc.Reason = old.Reason;
        }
        incoming.Warnings.Add($"Fuente actualizada: {retained} detalles conservados, {added} nuevos y {removed} retirados. Revisa los cambios antes de descargar.");
        return incoming;
    }
}
