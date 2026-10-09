using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using Portal.Application.Commercial.Reports;

namespace Portal.Infrastructure.Commercial.Reports;

public static class ReportExcel
{
    public const int MaximumBytes = 10 * 1024 * 1024;
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-CO");
    public static readonly string[] SalesHeaders = ["NUMERO", "FECHA", "NOMBRE", "PLAZO", "VALOR_BRUT", "IVA", "TOTAL", "DETALLE", "LINEA", "VENDEDOR", "NUMERO_OP", "NUMERO_REM"];
    public static readonly string[] OpHeaders = ["OP", "F. INGRESO", "COTIZACION", "OC", "NIT", "CLIENTE", "REFERENCIA", "LINEA DE PRODUCCION", "CODIGO PRODUCTO", "PRODUCTO", "MATERIAL", "TINTAS", "TAMAÑO", "TERMINADOS", "TIPO", "F. ENTREGA", "CÓD VENDEDOR", "VENDEDOR", "CANTIDAD", "VALOR UNITARIO", "VALOR TOTAL"];
    public static readonly string[] OutputHeaders = ["NUMERO OP", "FACTURA", "NUMERO", "FECHA", "NOMBRE", "PLAZO", "VALOR_BRUT", "DETALLE", "LINEA", "VENDEDOR"];

    public static XSSFWorkbook Open(byte[] bytes)
    {
        if (bytes.Length == 0 || bytes.Length > MaximumBytes) throw new ReportValidationException("El archivo debe ser Excel .xlsx y pesar como máximo 10 MB.");
        try
        {
            using var zip = new ZipArchive(new MemoryStream(bytes));
            if (zip.Entries.Count > 3000 || zip.Entries.Sum(e => e.Length) > 80L * 1024 * 1024 ||
                zip.Entries.Any(e => e.FullName.EndsWith("vbaProject.bin", StringComparison.OrdinalIgnoreCase)))
                throw new ReportValidationException("El archivo contiene macros o supera el tamaño permitido al abrirlo.");
            return new XSSFWorkbook(new MemoryStream(bytes));
        }
        catch (ReportValidationException) { throw; }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { throw new ReportValidationException("No fue posible abrir el Excel. Usa el archivo .xlsx original."); }
    }

    public static string Text(ICell? cell)
    {
        if (cell is null) return "";
        var type = cell.CellType == CellType.Formula ? cell.CachedFormulaResultType : cell.CellType;
        return type switch
        {
            CellType.String => cell.StringCellValue ?? "",
            CellType.Numeric => cell.NumericCellValue.ToString("0.################", CultureInfo.InvariantCulture),
            CellType.Boolean => cell.BooleanCellValue ? "SI" : "NO",
            _ => ""
        };
    }

    private static (ISheet Sheet, int Header, Dictionary<string, int> Columns) Find(XSSFWorkbook workbook, string[] headers)
    {
        for (var s = 0; s < workbook.NumberOfSheets; s++)
        {
            var sheet = workbook.GetSheetAt(s);
            for (var rowIndex = 0; rowIndex <= Math.Min(15, sheet.LastRowNum); rowIndex++)
            {
                var row = sheet.GetRow(rowIndex); if (row is null) continue;
                var columns = new Dictionary<string, int>();
                foreach (var cell in row.Cells) columns.TryAdd(SalesReportEngine.Normalize(Text(cell)), cell.ColumnIndex);
                if (headers.All(h => columns.ContainsKey(SalesReportEngine.Normalize(h)))) return (sheet, rowIndex, columns);
            }
        }
        throw new ReportValidationException("No se encontraron las columnas esperadas: " + string.Join(", ", headers) + ".");
    }

    private static bool MissingDateAndAmount(IRow row, Dictionary<string, int> columns) =>
        string.IsNullOrWhiteSpace(Text(row.GetCell(columns[SalesReportEngine.Normalize("FECHA")]))) &&
        string.IsNullOrWhiteSpace(Text(row.GetCell(columns[SalesReportEngine.Normalize("VALOR_BRUT")])));

    private static ReportSourceRowEvidence SourceRow(ISheet sheet, int header, IRow row, string fileName) => new()
    {
        SourceFile = fileName,
        Sheet = sheet.SheetName,
        SourceRow = row.RowNum + 1,
        Cells = Enumerable.Range(0, Math.Max(sheet.GetRow(header).LastCellNum, row.LastCellNum))
            .Select(column => new ReportSourceCellEvidence(
                NPOI.SS.Util.CellReference.ConvertNumToColString(column),
                Text(sheet.GetRow(header).GetCell(column)), Text(row.GetCell(column))))
            .ToArray()
    };

    private static ReportSourceWarningEvidence OmittedRowsEvidence(ReportSourceRowEvidence[] rows) => new()
    {
        Warning = $"Se omitieron {rows.Length} filas sin datos de FECHA ni VALOR_BRUT (textos sueltos o pies del archivo).",
        Rows = rows
    };

    // Read-only recovery for snapshots prepared before exact source-row evidence
    // was retained. It does not parse/rebuild accepted sales or evaluate formulas.
    public static ReportSourceWarningEvidence ReadOmittedSalesRows(byte[] bytes, string fileName)
    {
        using var workbook = Open(bytes);
        var (sheet, header, columns) = Find(workbook, SalesHeaders);
        if (sheet.LastRowNum > 25000) throw new ReportValidationException("El archivo supera 25.000 filas. Divide la carga.");
        var rows = new List<ReportSourceRowEvidence>();
        for (var index = header + 1; index <= sheet.LastRowNum; index++)
        {
            var row = sheet.GetRow(index);
            if (row is null || row.Cells.All(cell => string.IsNullOrWhiteSpace(Text(cell)))) continue;
            if (MissingDateAndAmount(row, columns)) rows.Add(SourceRow(sheet, header, row, fileName));
        }
        return OmittedRowsEvidence(rows.ToArray());
    }

    public static SalesReportData ReadSales(byte[] bytes, Guid sourceId, string fileName, string hash, bool preparationMode = false)
    {
        using var workbook = Open(bytes);
        var (sheet, header, columns) = Find(workbook, SalesHeaders);
        if (sheet.LastRowNum > 25000) throw new ReportValidationException("El archivo supera 25.000 filas. Divide la carga.");
        var result = new SalesReportData { CurrentSourceId = sourceId, SourceFile = fileName, Sha256 = hash };
        var ignored = 0;
        var omittedRows = new List<ReportSourceRowEvidence>();
        for (var i = header + 1; i <= sheet.LastRowNum; i++)
        {
            var row = sheet.GetRow(i); if (row is null) continue;
            ICell? Cell(string name) => row.GetCell(columns[SalesReportEngine.Normalize(name)]);
            string Value(string name) => Text(Cell(name));
            var number = Value("NUMERO").Trim();
            if (row.Cells.All(c => string.IsNullOrWhiteSpace(Text(c)))) continue;
            if (!preparationMode && (number.Length == 0 || number.Length > 100 || !number.All(char.IsDigit))) { ignored++; continue; }
            if (preparationMode && MissingDateAndAmount(row, columns))
            {
                ignored++;
                omittedRows.Add(SourceRow(sheet, header, row, fileName));
                continue;
            }
            if (number.Length > 100) throw new ReportValidationException($"Revisa NUMERO en la fila {i + 1}.");
            var dateCell = Cell("FECHA"); var dateText = Value("FECHA");
            DateTime date;
            if (dateCell is not null && (dateCell.CellType == CellType.Numeric || dateCell.CellType == CellType.Formula && dateCell.CachedFormulaResultType == CellType.Numeric))
            {
                try { date = DateUtil.GetJavaDate(dateCell.NumericCellValue); }
                catch { throw new ReportValidationException($"Revisa FECHA en la fila {i + 1}."); }
            }
            else if (!DateTime.TryParseExact(dateText.Trim(), ["yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "yyyy-MM-dd HH:mm:ss"], Spanish, DateTimeStyles.None, out date))
                throw new ReportValidationException($"Revisa FECHA en la fila {i + 1}.");
            var amountCell = Cell("VALOR_BRUT"); decimal amount;
            if (amountCell is not null && amountCell.CellType == CellType.Numeric) amount = Convert.ToDecimal(amountCell.NumericCellValue);
            else if (!decimal.TryParse(Value("VALOR_BRUT"), NumberStyles.Number, CultureInfo.InvariantCulture, out amount))
                throw new ReportValidationException($"Revisa VALOR_BRUT en la fila {i + 1}.");
            var client = Value("NOMBRE").Trim(); var detail = Value("DETALLE");
            if (!preparationMode && (client.Length == 0 || detail.Length == 0)) throw new ReportValidationException($"Completa NOMBRE y DETALLE en la fila {i + 1} del archivo original.");
            var item = new SalesDetail
            {
                Id = SalesReportEngine.Key(hash, sheet.SheetName, (i + 1).ToString()), SourceId = sourceId,
                SourceFile = fileName, Sheet = sheet.SheetName, SourceRow = i + 1, Number = number,
                Date = date.ToString("yyyy-MM-dd"), Client = client, Term = Value("PLAZO"), RawAmount = amount,
                Detail = detail, Line = Value("LINEA"), Seller = Value("VENDEDOR"), ManagerOp = Value("NUMERO_OP").Trim()
            };
            item.DocumentId = SalesReportEngine.Key(number, item.Date, SalesReportEngine.Normalize(client));
            item.MatchKey = SalesReportEngine.Key(number, item.Date, client, item.Term, amount.ToString(CultureInfo.InvariantCulture), detail, item.Line, item.Seller, item.ManagerOp);
            result.Details.Add(item);
        }
        if (result.Details.Count == 0) throw new ReportValidationException("El archivo no contiene ventas válidas.");
        result.Documents = result.Details.GroupBy(d => d.DocumentId).Select(g => new SalesDocument
        {
            Id = g.Key, Number = g.First().Number, Client = g.First().Client,
            SourceAmount = g.Select(d => d.RawAmount).Distinct().Count() == 1 ? g.First().RawAmount : null
        }).ToList();
        if (ignored > 0) result.Warnings.Add(preparationMode
            ? OmittedRowsEvidence(omittedRows.ToArray()).Warning
            : $"Se omitieron {ignored} filas sin un NUMERO válido (textos sueltos o pies del archivo).");
        if (omittedRows.Count > 0) result.SourceWarningEvidence.Add(OmittedRowsEvidence(omittedRows.ToArray()));
        if (!preparationMode) result.Warnings.Add("VALOR_BRUT puede repetirse entre detalles. Confirma el total de cada NUMERO y distribúyelo sin sumar esas repeticiones.");
        return result;
    }

    public static (int Row, OpRecordData Data)[] ReadOps(byte[] bytes, bool allowIncomplete = false)
    {
        using var workbook = Open(bytes);
        var (sheet, header, columns) = Find(workbook, OpHeaders);
        // F/G are the agreed historical reference, regardless of names used conversationally.
        if (allowIncomplete && (columns[SalesReportEngine.Normalize("OP")] != 0 || columns[SalesReportEngine.Normalize("CLIENTE")] != 5 ||
            columns[SalesReportEngine.Normalize("REFERENCIA")] != 6))
            throw new ReportValidationException("La plantilla del Informe de OPs debe conservar OP en A, CLIENTE en F y REFERENCIA en G.");
        if (sheet.LastRowNum > 25000) throw new ReportValidationException("El registro supera 25.000 filas.");
        var records = new List<(int, OpRecordData)>();
        for (var i = header + 1; i <= sheet.LastRowNum; i++)
        {
            var row = sheet.GetRow(i); if (row is null) continue;
            var cells = OpHeaders.Select(h => Text(row.GetCell(columns[SalesReportEngine.Normalize(h)]))).ToArray();
            if (string.IsNullOrWhiteSpace(cells[0])) continue;
            if (cells[0].Length > 100) throw new ReportValidationException($"Revisa OP en la fila {i + 1}; supera 100 caracteres.");
            if (!allowIncomplete && (cells[5].Length > 500 || cells[9].Length > 500))
                throw new ReportValidationException($"Revisa CLIENTE o PRODUCTO en la fila {i + 1}.");
            if (cells.Any(c => c.Length > ReportPreparationEngine.ExcelCellLimit))
                throw new ReportValidationException($"La fila {i + 1} supera el límite de caracteres de una celda de Excel.");
            // Preserve cached dates as dates, without evaluating formulas from the source.
            foreach (var index in new[] { 1, 15 })
                if (double.TryParse(cells[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var serial) && serial is > 20000 and < 100000)
                    cells[index] = DateUtil.GetJavaDate(serial).ToString("yyyy-MM-dd");
            records.Add((i + 1, new OpRecordData { Cells = cells }));
        }
        if (records.Count == 0) throw new ReportValidationException("No se encontraron números de OP válidos.");
        return records.ToArray();
    }

    public static byte[] ExportOps(IEnumerable<OpRecordDto> records)
    {
        using var book = new XSSFWorkbook(); var sheet = book.CreateSheet("OP ");
        var header = sheet.CreateRow(1); for (var i = 0; i < OpHeaders.Length; i++) header.CreateCell(i).SetCellValue(OpHeaders[i]);
        var index = 2;
        foreach (var record in records)
        {
            var row = sheet.CreateRow(index++);
            for (var i = 0; i < 21; i++) row.CreateCell(i).SetCellValue(record.Data.Cells.ElementAtOrDefault(i) ?? "");
        }
        sheet.CreateFreezePane(2, 2); for (var i = 0; i < 21; i++) sheet.SetColumnWidth(i, (i is 5 or 9 ? 40 : 20) * 256);
        using var stream = new MemoryStream(); book.Write(stream, true); return stream.ToArray();
    }

    public static byte[] ExportSales(SalesGroup[] groups, bool incompleteTotals = false,
        bool draft = false, bool previousVersion = false)
    {
        if (groups.Length == 0) throw new ReportValidationException("El reporte no contiene filas para descargar.");
        foreach (var group in groups) ReportPreparationEngine.ValidateRow(group);
        using var template = Assembly.GetExecutingAssembly().GetManifestResourceStream("Portal.Reports.VentasTemplate.xlsx")
            ?? throw new ReportValidationException("La plantilla de ventas no está disponible.", 503);
        using var templateBuffer = new MemoryStream(); template.CopyTo(templateBuffer);
        templateBuffer.Position = 0;
        Dictionary<string, byte[]> nativeCharts;
        using (var archive = new System.IO.Compression.ZipArchive(templateBuffer, System.IO.Compression.ZipArchiveMode.Read, leaveOpen: true))
            nativeCharts = archive.Entries.Where(e => e.FullName.StartsWith("xl/charts/", StringComparison.Ordinal))
                .ToDictionary(e => e.FullName, e => { using var data = new MemoryStream(); using var content = e.Open(); content.CopyTo(data); return data.ToArray(); });
        templateBuffer.Position = 0;
        using var book = new XSSFWorkbook(templateBuffer);
        var sheet = book.GetSheetAt(0);
        if (draft)
        {
            // A1 is a title, outside the B3:K sales header and data ranges. No
            // commercial column or dependent sheet is changed to mark a draft.
            var title = sheet.GetRow(0) ?? sheet.CreateRow(0);
            var label = title.GetCell(0) ?? title.CreateCell(0);
            label.SetCellValue(previousVersion ? "BORRADOR · VERSIÓN ANTERIOR · PENDIENTE DE APROBACIÓN"
                : "BORRADOR · PENDIENTE DE APROBACIÓN");
            var titleStyle = book.CreateCellStyle();
            titleStyle.Alignment = HorizontalAlignment.Left;
            titleStyle.VerticalAlignment = VerticalAlignment.Center;
            var font = book.CreateFont(); font.IsBold = true; font.Color = IndexedColors.DarkRed.Index;
            font.FontHeightInPoints = 12; titleStyle.SetFont(font); label.CellStyle = titleStyle;
            title.HeightInPoints = 25;
            sheet.AddMergedRegion(new NPOI.SS.Util.CellRangeAddress(0, 0, 0, 10));
            sheet.Header.Center = "BORRADOR - PENDIENTE DE APROBACIÓN";
        }
        var styles = Enumerable.Range(1, 10).Select(i => sheet.GetRow(3)?.GetCell(i)?.CellStyle).ToArray();
        // The bundled template contains no sales. Keep only its agreed layout and fulfillment formulas.
        for (var i = sheet.LastRowNum; i >= 3; i--) if (sheet.GetRow(i) is { } row) sheet.RemoveRow(row);
        var dateStyle = book.CreateCellStyle();
        if (styles[3] is { } originalDateStyle) dateStyle.CloneStyleFrom(originalDateStyle);
        else dateStyle.DataFormat = book.CreateDataFormat().GetFormat("dd/mm/yyyy");
        var moneyStyle = book.CreateCellStyle();
        if (styles[6] is { } originalMoneyStyle) moneyStyle.CloneStyleFrom(originalMoneyStyle);
        else moneyStyle.DataFormat = book.CreateDataFormat().GetFormat("#,##0.00");
        var detailStyle = book.CreateCellStyle();
        if (styles[7] is { } originalDetailStyle) detailStyle.CloneStyleFrom(originalDetailStyle);
        detailStyle.WrapText = true;
        for (var i = 0; i < groups.Length; i++)
        {
            var group = groups[i]; var row = sheet.CreateRow(i + 3);
            string[] values = [group.Op, group.Factura, group.Number, group.Date, group.Client, group.Term, "", string.Join("\n", group.Details), group.Line, group.Seller];
            for (var c = 0; c < 10; c++) { var cell = row.CreateCell(c + 1); cell.SetCellValue(values[c]); if (styles[c] is not null) cell.CellStyle = styles[c]; }
            if (group.Amount.HasValue) row.GetCell(7).SetCellValue(Convert.ToDouble(group.Amount.Value));
            else row.GetCell(7).SetCellType(CellType.Blank);
            row.GetCell(7).CellStyle = moneyStyle;
            if (group.Date.Length > 0 && DateTime.TryParseExact(group.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                row.GetCell(4).SetCellValue(date);
            else if (group.Date.Length == 0) row.GetCell(4).SetCellType(CellType.Blank);
            else throw new ReportValidationException("La FECHA de una fila no es válida para descargar.");
            row.GetCell(4).CellStyle = dateStyle;
            row.GetCell(8).CellStyle = detailStyle;
            row.HeightInPoints = Math.Min(180, Math.Max(30, group.Details.Length * 18));
        }
        var last = groups.Length + 3;
        var missingAmounts = groups.Any(g => !g.Amount.HasValue);
        var subtotal = $"SUBTOTAL(9,H4:H{last})";
        sheet.GetRow(1).GetCell(7).SetCellFormula(incompleteTotals && !missingAmounts ? "\"\""
            : missingAmounts ? $"IF(COUNT(H4:H{last})<{groups.Length},\"\",{subtotal})" : subtotal);
        sheet.CreateFreezePane(2, 3);
        sheet.SetAutoFilter(new NPOI.SS.Util.CellRangeAddress(2, last - 1, 1, 10));
        var fulfillment = book.GetSheet("CUMPLIMIENTO");
        if (fulfillment is not null)
            foreach (IRow row in fulfillment)
                foreach (var cell in row.Cells.Where(c => c.CellType == CellType.Formula))
                {
                    var formula = System.Text.RegularExpressions.Regex.Replace(cell.CellFormula,
                        @"(\$?[HJK]\$?4):(\$?[HJK]\$?)861", m => m.Groups[1].Value + ":" + m.Groups[2].Value + last);
                    if (incompleteTotals || missingAmounts) formula = $"IF('VENTAS '!$H$2=\"\",\"\",{formula})";
                    cell.SetCellFormula(formula);
                }
        try { book.GetCreationHelper().CreateFormulaEvaluator().EvaluateAll(); }
        catch { throw new ReportValidationException("No se pudieron comprobar las fórmulas de la plantilla. El borrador está guardado; revisa la configuración.", 422); }
        book.SetForceFormulaRecalculation(true);
        using var stream = new MemoryStream(); book.Write(stream, true);
        // The chart references fixed CUMPLIMIENTO cells. Preserve its sanitized
        // native XML instead of NPOI's incompatible chart serialization.
        using (var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Update, leaveOpen: true))
            foreach (var (name, content) in nativeCharts)
            {
                archive.GetEntry(name)?.Delete();
                using var restored = archive.CreateEntry(name).Open(); restored.Write(content);
            }
        return stream.ToArray();
    }
}
