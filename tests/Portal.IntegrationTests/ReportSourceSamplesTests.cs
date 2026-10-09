using System.Security.Cryptography;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using Portal.Application.Commercial.Reports;
using Portal.Infrastructure.Commercial.Reports;
namespace Portal.IntegrationTests;

public sealed class ReportSourceSamplesTests
{
    private static XSSFWorkbook EmbeddedVentasTemplate()
    {
        using var stream = typeof(ReportExcel).Assembly.GetManifestResourceStream("Portal.Reports.VentasTemplate.xlsx")!;
        return new XSSFWorkbook(stream);
    }

    private static string[] FormulaCoordinates(ISheet sheet) => sheet.Cast<IRow>()
        .SelectMany(row => row.Cells).Where(cell => cell.CellType == CellType.Formula)
        .Select(cell => cell.Address.FormatAsString()).Order().ToArray();

    [Fact]
    public void Final_export_preserves_the_VENTAS_MES_structure_and_typed_body_styles()
    {
        using var template = EmbeddedVentasTemplate();
        SalesGroup[] rows =
        [
            new("row-a", "doc-a", "500", "2026-10-07", "000450 / 000451", "", "Cliente de prueba", "CONTADO",
                100m, "", "Línea A", "Comercial A", ["Detalle uno", "Detalle dos"], ["detail-a", "detail-b"], [], false),
            new("row-b", "doc-b", "501", "2026-10-08", "000452", "", "Otro cliente de prueba", "30",
                250m, "Dato manual", "Línea B", "Comercial B", ["Detalle independiente"], ["detail-c"], [], false)
        ];
        using var exported = new XSSFWorkbook(new MemoryStream(ReportExcel.ExportSales(rows)));
        Assert.Equal(new[] { "VENTAS ", "CUMPLIMIENTO" }, Enumerable.Range(0, exported.NumberOfSheets).Select(exported.GetSheetName));
        var sheet = exported.GetSheetAt(0);
        var templateSheet = template.GetSheetAt(0);
        Assert.Equal(new[] { "NUMERO OP", "FACTURA", "NUMERO", "FECHA", "NOMBRE", "PLAZO", "VALOR_BRUT", "DETALLE", "LINEA", "VENDEDOR" },
            Enumerable.Range(1, 10).Select(column => sheet.GetRow(2).GetCell(column).StringCellValue));
        Assert.Equal("TOTAL", sheet.GetRow(1).GetCell(5).StringCellValue.Trim());
        Assert.Equal("SUBTOTAL(9,H4:H5)", sheet.GetRow(1).GetCell(7).CellFormula);
        Assert.Equal(350, sheet.GetRow(1).GetCell(7).NumericCellValue);
        Assert.Equal("000450 / 000451", sheet.GetRow(3).GetCell(1).StringCellValue);
        Assert.Equal("", sheet.GetRow(3).GetCell(2).StringCellValue); // Optional FACTURA remains empty.
        Assert.Equal("500", sheet.GetRow(3).GetCell(3).StringCellValue);
        Assert.Equal(CellType.Numeric, sheet.GetRow(3).GetCell(4).CellType);
        Assert.Equal(DateUtil.GetExcelDate(new DateTime(2026, 10, 7)), sheet.GetRow(3).GetCell(4).NumericCellValue);
        Assert.Equal("Cliente de prueba", sheet.GetRow(3).GetCell(5).StringCellValue);
        Assert.Equal("CONTADO", sheet.GetRow(3).GetCell(6).StringCellValue);
        Assert.Equal(CellType.Numeric, sheet.GetRow(3).GetCell(7).CellType);
        Assert.Equal(100, sheet.GetRow(3).GetCell(7).NumericCellValue);
        Assert.Equal("Detalle uno\nDetalle dos", sheet.GetRow(3).GetCell(8).StringCellValue);
        Assert.Equal("Línea A", sheet.GetRow(3).GetCell(9).StringCellValue);
        Assert.Equal("Comercial A", sheet.GetRow(3).GetCell(10).StringCellValue);

        // Numeric/date writes must preserve the supplied table's typography, borders and formats.
        for (var column = 1; column <= 10; column++)
        {
            Assert.Equal(templateSheet.GetColumnWidth(column), sheet.GetColumnWidth(column));
            var expected = templateSheet.GetRow(3).GetCell(column).CellStyle;
            var actual = sheet.GetRow(3).GetCell(column).CellStyle;
            Assert.Equal(expected.GetDataFormatString(), actual.GetDataFormatString());
            Assert.Equal(expected.BorderLeft, actual.BorderLeft);
            Assert.Equal(expected.BorderRight, actual.BorderRight);
            Assert.Equal(expected.BorderTop, actual.BorderTop);
            Assert.Equal(expected.BorderBottom, actual.BorderBottom);
            Assert.Equal(expected.FillForegroundColor, actual.FillForegroundColor);
            Assert.Equal(expected.FillPattern, actual.FillPattern);
            Assert.Equal(expected.Alignment, actual.Alignment);
            Assert.Equal(expected.VerticalAlignment, actual.VerticalAlignment);
            Assert.Equal(expected.WrapText, actual.WrapText);
            var expectedFont = template.GetFontAt(expected.FontIndex);
            var actualFont = exported.GetFontAt(actual.FontIndex);
            Assert.Equal(expectedFont.FontName, actualFont.FontName);
            Assert.Equal(expectedFont.FontHeightInPoints, actualFont.FontHeightInPoints);
            Assert.Equal(expectedFont.IsBold, actualFont.IsBold);
            Assert.Equal(expectedFont.Color, actualFont.Color);
        }
        var fulfillment = exported.GetSheetAt(1);
        Assert.Equal(111, FormulaCoordinates(fulfillment).Length);
        Assert.Equal(FormulaCoordinates(template.GetSheetAt(1)), FormulaCoordinates(fulfillment));
        Assert.Equal("B23:I23", fulfillment.GetMergedRegion(0).FormatAsString());
    }

    private sealed class SourceSampleFactAttribute : FactAttribute
    {
        public SourceSampleFactAttribute()
        { if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PORTAL_REPORT_FIXTURE_DIR"))) Skip = "Optional local source workbooks; never committed or transferred."; }
    }
    [SourceSampleFact]
    public void Supplied_workbooks_are_read_without_changes_and_repeated_values_are_not_summed()
    {
        var directory = Environment.GetEnvironmentVariable("PORTAL_REPORT_FIXTURE_DIR")!;
        var managerPath = Path.Combine(directory, "INFORME DE VENTAS (MANAGER).xlsx");
        var opsPath = Path.Combine(directory, "INFORME DE OPS.xlsx");
        var originalManager = File.ReadAllBytes(managerPath); var originalOps = File.ReadAllBytes(opsPath);
        var sales = ReportExcel.ReadSales(originalManager, Guid.NewGuid(), "Manager.xlsx", "local-source");
        var ops = ReportExcel.ReadOps(originalOps);
        Assert.Equal(312, sales.Details.Count); Assert.Equal(152, sales.Details.Count(d => d.ManagerOp.Length == 0));
        Assert.Equal(3, sales.Details.Count(d => d.Number == "8877"));
        Assert.Equal(823500, sales.Documents.Single(d => d.Number == "8877").SourceAmount);
        Assert.Equal(4322, ops.Length);
        Assert.Equal(2, ops.Count(r => r.Data.Cells[0] == "24852"));
        Assert.Equal(3, ops.Count(r => new[] { "25273", "25279", "25280" }.Contains(r.Data.Cells[0])));
        Assert.Equal(SHA256.HashData(originalManager), SHA256.HashData(File.ReadAllBytes(managerPath)));
        Assert.Equal(SHA256.HashData(originalOps), SHA256.HashData(File.ReadAllBytes(opsPath)));
    }

    [SourceSampleFact]
    public void New_preparation_keeps_sales_separate_and_preserves_candidates_until_an_explicit_human_decision()
    {
        var directory = Environment.GetEnvironmentVariable("PORTAL_REPORT_FIXTURE_DIR")!;
        var managerPath = Path.Combine(directory, "INFORME DE VENTAS (MANAGER).xlsx");
        var opsPath = Path.Combine(directory, "INFORME DE OPS.xlsx");
        var managerBytes = File.ReadAllBytes(managerPath); var opsBytes = File.ReadAllBytes(opsPath);
        var sales = ReportExcel.ReadSales(managerBytes, Guid.NewGuid(), "Manager.xlsx", "local-source", preparationMode: true);
        var history = ReportExcel.ReadOps(opsBytes).Select(r => new OpRecordDto(Guid.NewGuid(), r.Data.Cells[0], "",
            r.Data.Cells[5], r.Data.Cells[9], null, DateTimeOffset.UnixEpoch.AddSeconds(r.Row), "Registro histórico", r.Data)).ToList();
        Assert.Equal("CLIENTE", ReportExcel.OpHeaders[5]);
        Assert.Equal("REFERENCIA", ReportExcel.OpHeaders[6]);
        var result = ReportPreparationEngine.Prepare(sales, history);

        Assert.Equal(4, result.RuleVersion);
        Assert.NotNull(result.Review);
        var fileIncidence = Assert.Single(result.Review.Cases, c => c.Scope == "file");
        var omittedRow = Assert.Single(Assert.Single(fileIncidence.Findings).SourceRows);
        Assert.Equal("Hoja1", omittedRow.Sheet);
        Assert.Equal(314, omittedRow.SourceRow);
        var populatedCell = Assert.Single(omittedRow.Cells, cell => !string.IsNullOrWhiteSpace(cell.Value));
        Assert.Equal("A", populatedCell.Column);
        Assert.Equal("NUMERO", populatedCell.Header);
        Assert.Equal("pending", fileIncidence.Findings[0].Resolution);
        Assert.Equal(312, result.Rows.SelectMany(r => r.DetailIds).Count());
        Assert.Equal(sales.Details.Select(d => d.Id).Order(), result.Rows.SelectMany(r => r.DetailIds).Order());
        Assert.All(sales.Details.Where(d => d.ManagerOp.Length == 0), detail =>
        {
            var row = Assert.Single(result.Rows, r => r.DetailIds.Contains(detail.Id));
            Assert.Single(row.DetailIds);
            Assert.Equal("N/A", row.Op);
            var pending = Assert.Single(result.Review.Cases, c => c.RowKey == row.Key);
            Assert.Contains(pending.Findings, f => f.Field == "NUMERO OP" && f.Resolution == "pending");
        });
        var distinctSales = result.Rows.Where(r => r.Number is "8889" or "8890").ToArray();
        Assert.Equal(2, distinctSales.Length);
        Assert.All(distinctSales, row => Assert.NotNull(row.Amount));
        var repeatedDetails = sales.Details.Where(d => d.Number == "8877").ToArray();
        var consolidated = Assert.Single(result.Rows, r => repeatedDetails.All(d => r.DetailIds.Contains(d.Id)));
        Assert.Equal(823500, consolidated.Amount);
        Assert.Equal(repeatedDetails.Select(d => d.Detail), consolidated.Details);
        Assert.Equal("N/A", consolidated.Op);
        var reviewCase = Assert.Single(result.Review.Cases, c => c.RowKey == consolidated.Key);
        var opFindings = reviewCase.Findings.Where(f => f.Field == "NUMERO OP").ToArray();
        var candidateNumbers = opFindings.SelectMany(f => f.Evidence).SelectMany(e => e.Candidates)
            .Select(candidate => candidate.Number).ToHashSet();
        var expectedNumbers = new[] { "25273", "25279", "25280" };
        Assert.All(expectedNumbers, number => Assert.Contains(number, candidateNumbers));
        Assert.All(opFindings, finding => Assert.Equal("pending", finding.Resolution));
        var selectedIds = history.Where(h => expectedNumbers.Contains(h.Number)).Select(h => h.Id).ToArray();
        Assert.Equal(3, selectedIds.Length);
        ReportReviewPolicy.ApplyDecisions(sales,
            [new ReportReviewCommand(reviewCase.Id, opFindings.Select(f => f.Id).ToArray(), "select_candidates", selectedIds)],
            "auxiliar-de-prueba", 2, DateTimeOffset.UnixEpoch);
        var decided = sales.Preparation!;
        var resolved = Assert.Single(decided.Rows, r => r.Key == consolidated.Key);
        Assert.Equal("25273/25279/25280", resolved.Op.Replace(" ", ""));
        Assert.Equal(repeatedDetails.Select(d => d.Detail), resolved.Details);
        Assert.Equal(823500, resolved.Amount);
        var decidedCase = Assert.Single(decided.Review!.Cases, c => c.Id == reviewCase.Id);
        Assert.All(decidedCase.Findings.Where(f => f.Field == "NUMERO OP"), finding => { Assert.Equal("resolved", finding.Resolution); Assert.Equal("human", finding.Provenance); });
        Assert.Contains(decided.Review.Decisions, decision => decision.Actor == "auxiliar-de-prueba" && decision.Action == "select_candidates");
        Assert.Equal(SHA256.HashData(managerBytes), SHA256.HashData(File.ReadAllBytes(managerPath)));
        Assert.Equal(SHA256.HashData(opsBytes), SHA256.HashData(File.ReadAllBytes(opsPath)));
    }

    [SourceSampleFact]
    public void The_reusable_template_matches_the_supplied_final_workbook_without_copying_its_sales()
    {
        var directory = Environment.GetEnvironmentVariable("PORTAL_REPORT_FIXTURE_DIR")!;
        var finalPath = Path.Combine(directory, "VENTAS MES.xlsx");
        var original = File.ReadAllBytes(finalPath);
        using var supplied = ReportExcel.Open(original);
        using var reusable = EmbeddedVentasTemplate();
        Assert.Equal(Enumerable.Range(0, supplied.NumberOfSheets).Select(supplied.GetSheetName),
            Enumerable.Range(0, reusable.NumberOfSheets).Select(reusable.GetSheetName));
        var finalSheet = supplied.GetSheetAt(0);
        var reusableSheet = reusable.GetSheetAt(0);
        Assert.Equal(Enumerable.Range(1, 10).Select(column => finalSheet.GetRow(2).GetCell(column).StringCellValue),
            Enumerable.Range(1, 10).Select(column => reusableSheet.GetRow(2).GetCell(column).StringCellValue));
        Assert.Equal(finalSheet.GetRow(1).GetCell(5).StringCellValue, reusableSheet.GetRow(1).GetCell(5).StringCellValue);
        Assert.Equal(finalSheet.GetRow(1).GetCell(7).CellFormula, reusableSheet.GetRow(1).GetCell(7).CellFormula);
        Assert.Equal(FormulaCoordinates(supplied.GetSheetAt(1)), FormulaCoordinates(reusable.GetSheetAt(1)));
        Assert.All(reusableSheet.Cast<IRow>().Where(row => row.RowNum >= 3).SelectMany(row => row.Cells),
            cell => Assert.True(string.IsNullOrEmpty(ReportExcel.Text(cell))));
        Assert.Equal(SHA256.HashData(original), SHA256.HashData(File.ReadAllBytes(finalPath)));
    }
}
