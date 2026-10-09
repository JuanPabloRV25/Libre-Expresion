using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using NPOI.SS.UserModel;
using Portal.Application.Commercial.ProductionOrders;
using UglyToad.PdfPig;

namespace Portal.Infrastructure.Commercial.ProductionOrders;

public sealed partial class EmlazeQuotationImporter : IQuotationImporter
{
    public async Task<QuotationPreviewResult> PreviewAsync(UploadedFileCommand file, CancellationToken cancellationToken = default)
    {
        await using var memory = new MemoryStream();
        await file.Content.CopyToAsync(memory, cancellationToken);
        var bytes = memory.ToArray();
        var validation = ProductionOrderFileValidator.ValidateQuotation(file.FileName, bytes.LongLength, bytes.AsSpan(0, Math.Min(16, bytes.Length)));
        if (validation is not null) return Failure("quotation_file_invalid", validation);

        try
        {
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            var lines = extension == ".pdf" ? ReadPdf(bytes) : ReadWorkbook(bytes);
            return Parse(file.FileName, extension[1..], lines);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Failure("quotation_unreadable", "No fue posible leer la cotización. Verifica que el archivo no esté protegido o dañado.");
        }
    }

    private static QuotationPreviewResult Parse(string fileName, string format, IReadOnlyList<string> sourceLines)
    {
        var lines = sourceLines.Select(Clean).Where(line => line.Length > 0).ToArray();
        var text = string.Join(" ", lines);
        if (!text.Contains("cotiz", StringComparison.OrdinalIgnoreCase))
            return Failure("quotation_template_unknown", "El archivo no contiene una cotización reconocible.");

        var quotationNumber = Capture(text, @"Cotizaci[oó]n\s*N?[°ºo.]?\s*([0-9-]+)");
        var attentionIndex = Array.FindIndex(lines, line => line.StartsWith("Atn", StringComparison.OrdinalIgnoreCase));
        var clientName = attentionIndex > 0
            ? lines.Take(attentionIndex).Reverse().FirstOrDefault(line =>
                !line.StartsWith("Fecha", StringComparison.OrdinalIgnoreCase)
                && !line.StartsWith("Referencia", StringComparison.OrdinalIgnoreCase)
                && !line.StartsWith("Cotizaci", StringComparison.OrdinalIgnoreCase))
            : null;
        var nitIndex = Array.FindIndex(lines, line => line.StartsWith("NIT", StringComparison.OrdinalIgnoreCase));
        var address = nitIndex >= 0 ? lines.Skip(nitIndex + 1).FirstOrDefault(line => Regex.IsMatch(line, @"\d") && !line.StartsWith("Validez", StringComparison.OrdinalIgnoreCase)) : null;
        var addressIndex = address is null ? -1 : Array.IndexOf(lines, address);
        var city = addressIndex >= 0
            ? lines.Skip(addressIndex + 1).FirstOrDefault(line => !line.StartsWith("Tiempo de entrega", StringComparison.OrdinalIgnoreCase))
            : null;

        var items = ParseItems(lines);

        if (items.Count == 0)
            return Failure("quotation_items_not_found", "No se encontraron productos y opciones de cantidad en la cotización.");

        var warnings = new List<string>();
        if (string.IsNullOrWhiteSpace(quotationNumber)) warnings.Add("No se identificó el número de cotización.");
        if (string.IsNullOrWhiteSpace(clientName)) warnings.Add("No se identificó automáticamente el cliente.");
        var fingerprintSource = $"{format}|quotation|items|description|quantity|unit|net";
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprintSource))).ToLowerInvariant();
        return new QuotationPreviewResult(true, new QuotationPreviewDto(
            fileName, format, fingerprint, quotationNumber, clientName, city, address, items, warnings));
    }

    private static List<QuotationItemDto> ParseItems(IReadOnlyList<string> lines)
    {
        var starts = lines.Select((line, index) => (line, index))
            .Where(item => ItemStartRegex().IsMatch(item.line))
            .Select(item => item.index)
            .ToArray();
        var items = new List<QuotationItemDto>();
        for (var blockIndex = 0; blockIndex < starts.Length; blockIndex++)
        {
            var start = starts[blockIndex];
            var end = blockIndex + 1 < starts.Length ? starts[blockIndex + 1] : lines.Count;
            var block = lines.Skip(start).Take(end - start)
                .TakeWhile(line => !line.StartsWith("Condiciones comerciales", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var options = block.SelectMany(line => PriceOptionRegex().Matches(line))
                .Select(option => new QuotationPriceOptionDto(
                    ParseNumber(option.Groups["quantity"].Value),
                    ParseNumber(option.Groups["unit"].Value),
                    ParseNumber(option.Groups["net"].Value)))
                .Where(option => option.Quantity > 0 && option.UnitValue > 0)
                .Distinct()
                .ToArray();
            if (options.Length == 0) continue;

            var descriptionParts = block.Select(line => PriceOptionRegex().Replace(line, string.Empty))
                .Select(line => Regex.Replace(line, @"^\d+\s+(?=C\s*-)", string.Empty, RegexOptions.IgnoreCase))
                .Select(Clean)
                .Where(line => line.Length > 2 && !Regex.IsMatch(line, @"^\d+$"))
                .ToArray();
            var description = Clean(string.Join(" ", descriptionParts));
            items.Add(new QuotationItemDto(
                items.Count,
                description,
                ProductName(description),
                Capture(description, @"ABIERTA\s*:\s*([^\-]+?)(?=\s*\-\s*MATERIAL|$)"),
                Capture(description, @"MATERIAL\s*:\s*([^\-]+?)(?=\s*CAL|$)"),
                Capture(description, @"CAL\s*([0-9A-Z.]+)"),
                Capture(description, @"IMPRESI[OÓ]N\s*:\s*([0-9]+\s*X\s*[0-9]+)"),
                Capture(description, @"IMPRESI[OÓ]N\s*:[^()]*\(([^)]+)\)"),
                options));
        }
        return items;
    }

    private static IReadOnlyList<string> ReadPdf(byte[] bytes)
    {
        using var document = PdfDocument.Open(bytes);
        var lines = new List<string>();
        foreach (var page in document.GetPages())
        {
            var groups = page.GetWords()
                .GroupBy(word => Math.Round(word.BoundingBox.Bottom / 2d) * 2d)
                .OrderByDescending(group => group.Key);
            lines.AddRange(groups.Select(group => string.Join(" ", group.OrderBy(word => word.BoundingBox.Left).Select(word => word.Text))));
        }
        return lines;
    }

    private static IReadOnlyList<string> ReadWorkbook(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var workbook = WorkbookFactory.Create(stream);
        var formatter = new DataFormatter(CultureInfo.GetCultureInfo("es-CO"));
        var lines = new List<string>();
        for (var sheetIndex = 0; sheetIndex < workbook.NumberOfSheets; sheetIndex++)
        {
            var sheet = workbook.GetSheetAt(sheetIndex);
            for (var rowIndex = sheet.FirstRowNum; rowIndex <= sheet.LastRowNum; rowIndex++)
            {
                var row = sheet.GetRow(rowIndex);
                if (row is null) continue;
                var values = row.Cells.Select(cell => formatter.FormatCellValue(cell)).Where(value => !string.IsNullOrWhiteSpace(value));
                var line = string.Join(" ", values);
                if (!string.IsNullOrWhiteSpace(line)) lines.Add(line);
            }
        }
        return lines;
    }

    private static string? LineBefore(IReadOnlyList<string> lines, Func<string, bool> predicate)
    {
        for (var index = 1; index < lines.Count; index++)
            if (predicate(lines[index])) return lines[index - 1];
        return null;
    }

    private static string? LineAfter(IReadOnlyList<string> lines, Func<string, bool> predicate, int offset)
    {
        for (var index = 0; index + offset < lines.Count; index++)
            if (predicate(lines[index])) return lines[index + offset];
        return null;
    }

    private static string? Capture(string value, string pattern)
    {
        var match = Regex.Match(value, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? Clean(match.Groups[1].Value) : null;
    }

    private static string? ProductName(string description)
    {
        var value = Capture(description, @"PLEGADIZA\s*:\s*(.+?)(?=\s*:\s*MEDIDA|\s*-\s*MEDIDA|$)") ?? description;
        return value.Length <= 180 ? value : value[..180];
    }

    private static decimal ParseNumber(string value) =>
        decimal.TryParse(value.Replace(".", string.Empty).Replace(",", "."), NumberStyles.Number, CultureInfo.InvariantCulture, out var result)
            ? result
            : 0;

    private static string Clean(string value) => Regex.Replace(value, @"\s+", " ").Trim();

    private static QuotationPreviewResult Failure(string code, string message) => new(false, ErrorCode: code, ErrorMessage: message);

    [GeneratedRegex(@"^(?:\d+\s+)?C\s*-\s*PLEGADIZA", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ItemStartRegex();

    [GeneratedRegex(@"(?<quantity>\d{1,3}(?:\.\d{3})*)\s+\$\s*(?<unit>[\d.]+)\s+\$\s*(?<net>[\d.]+)(?:\s+\$\s*[\d.]+){0,2}", RegexOptions.CultureInvariant)]
    private static partial Regex PriceOptionRegex();
}
