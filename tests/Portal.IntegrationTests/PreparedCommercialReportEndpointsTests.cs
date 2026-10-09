using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using Portal.Application.Commercial.Reports;
using Portal.Domain.Commercial.ProductionOrders;
using Portal.Domain.Commercial.Reports;
using Portal.Infrastructure.Commercial.Reports;
using Portal.Infrastructure.Persistence;
using Portal.Infrastructure.Persistence.Seeding;

namespace Portal.IntegrationTests;

public sealed partial class CommercialProductionOrderEndpointsTests
{
    private static readonly JsonSerializerOptions ReportTestJson = new(JsonSerializerDefaults.Web);

    private sealed record PreparedSourceRow(string Op, string Detail, string Line = "PRODUCTO A",
        decimal Amount = 100, string Number = "500", string Client = "Cliente de ventas",
        string Date = "2026-10-07", string Term = "CONTADO", string Seller = "Comercial");

    private static byte[] PreparedManagerFile(params PreparedSourceRow[] rows)
    {
        using var book = new XSSFWorkbook();
        var sheet = book.CreateSheet("Hoja1");
        var header = sheet.CreateRow(0);
        for (var c = 0; c < ReportExcel.SalesHeaders.Length; c++)
            header.CreateCell(c).SetCellValue(ReportExcel.SalesHeaders[c]);
        for (var i = 0; i < rows.Length; i++)
        {
            var source = rows[i];
            var row = sheet.CreateRow(i + 1);
            string[] cells = [source.Number, source.Date, source.Client, source.Term,
                source.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "909090", "808080", source.Detail, source.Line, source.Seller, source.Op,
                "REM_EXCLUIDA"];
            for (var c = 0; c < cells.Length; c++) row.CreateCell(c).SetCellValue(cells[c]);
        }
        using var output = new MemoryStream();
        book.Write(output, true);
        return output.ToArray();
    }

    private static OpRecordData PreparedOpData(string number, string reference = "Referencia común",
        string historicalClient = "Cliente histórico", string product = "Otro producto histórico")
    {
        var cells = Enumerable.Repeat("", 21).ToArray();
        cells[0] = number;
        cells[1] = "2026-10-01";
        cells[5] = historicalClient; // F; deliberately different from Manager NOMBRE.
        cells[6] = reference;       // G; authoritative identity within the historical register.
        cells[9] = product;         // J must not substitute for F/G.
        cells[18] = "1";
        return new OpRecordData { Cells = cells };
    }

    private static byte[] PreparedOpsFile(params OpRecordData[] records)
    {
        using var book = new XSSFWorkbook();
        var sheet = book.CreateSheet("OP ");
        var header = sheet.CreateRow(1);
        for (var c = 0; c < ReportExcel.OpHeaders.Length; c++)
            header.CreateCell(c).SetCellValue(ReportExcel.OpHeaders[c]);
        for (var i = 0; i < records.Length; i++)
        {
            var row = sheet.CreateRow(i + 2);
            for (var c = 0; c < records[i].Cells.Length; c++)
                row.CreateCell(c).SetCellValue(records[i].Cells[c]);
        }
        using var output = new MemoryStream();
        book.Write(output, true);
        return output.ToArray();
    }

    private static async Task<Guid[]> SeedPreparedOpsAsync(PortalApiFactory factory, params OpRecordData[] records)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var entities = records.Select((data, i) => new ProductionOrderReportRecord
        {
            Id = Guid.NewGuid(), Number = data.Cells[0], Client = data.Cells[5], Product = data.Cells[9],
            DataJson = JsonSerializer.Serialize(data), CapturedByUserId = Guid.NewGuid(),
            CapturedAt = DateTimeOffset.UtcNow.AddSeconds(i - records.Length)
        }).ToArray();
        db.AddRange(entities);
        await db.SaveChangesAsync();
        return entities.Select(r => r.Id).ToArray();
    }

    private static async Task<Guid> SeedLegacyReportAsync(PortalApiFactory factory, Guid owner, byte[] bytes, Guid? op = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var source = new CommercialReportSource
        {
            Id = Guid.NewGuid(), OwnerUserId = owner, Kind = "manager", FileName = "Manager.xlsx",
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            OriginalBytes = bytes, CreatedAt = DateTimeOffset.UtcNow
        };
        var data = ReportExcel.ReadSales(bytes, source.Id, source.FileName, source.Sha256);
        if (op.HasValue)
        {
            var historical = await db.OpReportRecords.SingleAsync(r => r.Id == op);
            foreach (var detail in data.Details)
            {
                detail.SelectedOpId = op; detail.SelectedOpNumber = historical.Number;
                detail.SelectedProduct = historical.Product; detail.Reviewed = true;
            }
        }
        var report = new CommercialReport
        {
            Id = Guid.NewGuid(), OwnerUserId = owner, Name = "Reporte anterior",
            DataJson = JsonSerializer.Serialize(data, ReportTestJson),
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        source.ReportId = report.Id;
        db.AddRange(report, source);
        await db.SaveChangesAsync();
        return report.Id;
    }

    private static JsonElement Preparation(ReportDto report) =>
        JsonSerializer.SerializeToElement(report, ReportTestJson).GetProperty("data").GetProperty("preparation");

    private static async Task<ReportDto> CreatePreparedReportAsync(HttpClient client, byte[] bytes)
    {
        var response = await ReportFile(client, "/api/commercial/reports", bytes);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var report = (await response.Content.ReadFromJsonAsync<ReportDto>())!;
        Assert.Equal(4, Preparation(report).GetProperty("ruleVersion").GetInt32());
        Assert.True(report.CanExportDraft);
        Assert.False(report.CanExportFinal);
        Assert.Equal(JsonSerializer.SerializeToElement(report.Groups, ReportTestJson).GetRawText(),
            Preparation(report).GetProperty("rows").GetRawText());
        return report;
    }

    private static bool EmptyExcelCell(ICell? cell) => cell is null || cell.CellType == CellType.Blank
        || cell.CellType == CellType.String && cell.StringCellValue.Length == 0;

    private static object OpDecision(ReportDto report, string rowKey, string action,
        Guid[]? historyIds = null, string? op = null)
    {
        var reviewCase = Assert.Single(Preparation(report).GetProperty("review").GetProperty("cases").EnumerateArray(),
            c => c.GetProperty("rowKey").GetString() == rowKey);
        var findings = reviewCase.GetProperty("findings").EnumerateArray()
            .Where(f => f.GetProperty("field").GetString() == "NUMERO OP" && f.GetProperty("resolution").GetString() == "pending")
            .Select(f => f.GetProperty("id").GetString()!).ToArray();
        Assert.NotEmpty(findings);
        return new { caseId = reviewCase.GetProperty("id").GetString(), findingIds = findings, action, historyIds, op };
    }

    private static async Task<ReportDto> SavePreparedDecisionsAsync(HttpClient client, ReportDto report,
        params object[] decisions)
    {
        var response = await SendWithCsrfAsync(client, HttpMethod.Put, $"/api/commercial/reports/{report.Id}/prepared",
            new { version = report.Version, name = report.Name, rowEdits = Array.Empty<object>(), decisions });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ReportDto>())!;
    }

    private static async Task<ReportDto> ApprovePreparedAsync(HttpClient client, ReportDto report)
    {
        var response = await SendWithCsrfAsync(client, HttpMethod.Post, $"/api/commercial/reports/{report.Id}/approve",
            new { version = report.Version });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ReportDto>())!;
    }

    [Fact]
    public async Task Prepared_reports_keep_details_and_select_all_compatible_OPs_only_by_explicit_human_decision()
    {
        await using var factory = new PortalApiFactory(); var admin = await SeedSuperadminAsync(factory);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await LoginAsync(client, admin.Document);
        var ids = await SeedPreparedOpsAsync(factory, PreparedOpData("4500", "Detalle A"), PreparedOpData("4501", "Detalle B"),
            PreparedOpData("4502", "Detalle B"), PreparedOpData("9999", "Otra referencia", historicalClient: "Otra identidad"));
        var original = PreparedManagerFile(new("4500", "Detalle A"), new("4500", "Detalle B"),
            new("4500", "Detalle B"), new("4600", "OP independiente", "PRODUCTO B", 75));
        var report = await CreatePreparedReportAsync(client, original);

        Assert.Equal(4, report.Data.Details.Count);
        Assert.Equal(2, report.Groups.Length);
        var consolidated = Assert.Single(report.Groups, g => g.Line == "PRODUCTO A");
        Assert.Equal("N/A", consolidated.Op);
        Assert.DoesNotContain("9999", consolidated.Op);
        Assert.Equal(new[] { "Detalle A", "Detalle B", "Detalle B" }, consolidated.Details);
        Assert.Equal(3, consolidated.DetailIds.Length);
        Assert.Equal(100, consolidated.Amount); // Repeated value is kept once, never summed.
        Assert.Equal("Cliente de ventas", consolidated.Client);
        Assert.Equal("4600", Assert.Single(report.Groups, g => g.Line == "PRODUCTO B").Op);
        Assert.Equal("F01", consolidated.Factura);
        Assert.Equal("generated_with_observations", Preparation(report).GetProperty("status").GetString());
        Assert.NotEmpty(Preparation(report).GetProperty("changes").EnumerateArray());

        report = await SavePreparedDecisionsAsync(client, report,
            OpDecision(report, consolidated.Key, "select_candidates", ids[..3]));
        Assert.Equal("4500/4501/4502", report.Groups.Single(g => g.Key == consolidated.Key).Op);
        Assert.Null(report.Groups.Single(g => g.Key == consolidated.Key).Amount);
        var fillTotal = await SendWithCsrfAsync(client, HttpMethod.Put, $"/api/commercial/reports/{report.Id}/prepared", new
        { version = report.Version, rowEdits = new[] { new { key = consolidated.Key, setAmount = true, amount = 100m } } });
        Assert.True(fillTotal.IsSuccessStatusCode, await fillTotal.Content.ReadAsStringAsync());
        report = (await fillTotal.Content.ReadFromJsonAsync<ReportDto>())!;
        report = await ApprovePreparedAsync(client, report);

        var exported = await SendWithCsrfAsync(client, HttpMethod.Post, $"/api/commercial/reports/{report.Id}/export", new { version = report.Version });
        Assert.True(exported.IsSuccessStatusCode, await exported.Content.ReadAsStringAsync());
        using var workbook = new XSSFWorkbook(new MemoryStream(await exported.Content.ReadAsByteArrayAsync()));
        var sheet = workbook.GetSheet("VENTAS ");
        Assert.Equal(ReportExcel.OutputHeaders, Enumerable.Range(1, 10).Select(c => sheet.GetRow(2).GetCell(c).StringCellValue));
        Assert.Equal(report.Groups[0].Op, sheet.GetRow(3).GetCell(1).StringCellValue);
        Assert.Equal(string.Join('\n', report.Groups[0].Details), sheet.GetRow(3).GetCell(8).StringCellValue);
        Assert.Equal("F01", sheet.GetRow(3).GetCell(2).StringCellValue);
        Assert.Equal(CellType.Numeric, sheet.GetRow(3).GetCell(4).CellType);
        Assert.Equal(175, sheet.GetRow(1).GetCell(7).NumericCellValue);
        Assert.DoesNotContain("REM_EXCLUIDA", sheet.GetRow(3).Cells.Select(c => c.ToString()));
        Assert.Equal(original, await client.GetByteArrayAsync($"/api/commercial/reports/sources/{report.Data.CurrentSourceId}"));
    }

    [Fact]
    public async Task Prepared_reports_keep_blank_OPs_independent_and_allow_only_draft_while_pending()
    {
        await using var factory = new PortalApiFactory(); var admin = await SeedSuperadminAsync(factory);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await LoginAsync(client, admin.Document);
        await SeedPreparedOpsAsync(factory, PreparedOpData("4700", "Referencia A"), PreparedOpData("4700", "Referencia B"));
        var report = await CreatePreparedReportAsync(client, PreparedManagerFile(
            new("4700", "A1"), new("4700", "A2"), new("4700", "B1", "PRODUCTO B"),
            new("", "Sin número uno"), new("", "Sin número dos"),
            new("4800", "Sin línea uno", ""), new("4800", "Sin línea dos", "")));

        Assert.Equal(6, report.Groups.Length);
        var provisional = Assert.Single(report.Groups, g => g.Details.Length == 2);
        Assert.Equal("N/A", provisional.Op);
        Assert.NotEmpty(provisional.Issues);
        Assert.Equal(3, report.Groups.Count(g => g.Op == "N/A"));
        Assert.Equal(2, report.Groups.Count(g => g.Line == ""));
        Assert.Equal("4700", Assert.Single(report.Groups, g => g.Line == "PRODUCTO B").Op);
        Assert.Equal("generated_with_observations", Preparation(report).GetProperty("status").GetString());
        var export = await SendWithCsrfAsync(client, HttpMethod.Post, $"/api/commercial/reports/{report.Id}/export", new { version = report.Version });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, export.StatusCode);
        var draft = await SendWithCsrfAsync(client, HttpMethod.Post, $"/api/commercial/reports/{report.Id}/export/draft", new { version = report.Version });
        Assert.True(draft.IsSuccessStatusCode, await draft.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Prepared_reports_do_not_fabricate_heterogeneous_metadata_amounts_or_complete_totals()
    {
        await using var factory = new PortalApiFactory(); var admin = await SeedSuperadminAsync(factory);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await LoginAsync(client, admin.Document);
        var report = await CreatePreparedReportAsync(client, PreparedManagerFile(
            new("4900", "Detalle uno", Amount: 100, Number: "500", Client: "Cliente A", Date: "2026-10-06"),
            new("4900", "Detalle dos", Amount: 200, Number: "500", Client: "Cliente B", Date: "2026-10-07")));
        var row = Assert.Single(report.Groups);
        Assert.Null(row.Amount);
        Assert.Equal("500", row.Number); Assert.Equal("", row.Client); Assert.Equal("", row.Date);
        Assert.Equal(2, row.DetailIds.Length);
        Assert.NotEmpty(row.Issues);
        var export = await SendWithCsrfAsync(client, HttpMethod.Post, $"/api/commercial/reports/{report.Id}/export/draft", new { version = report.Version });
        Assert.True(export.IsSuccessStatusCode, await export.Content.ReadAsStringAsync());
        using var workbook = new XSSFWorkbook(new MemoryStream(await export.Content.ReadAsByteArrayAsync()));
        var sheet = workbook.GetSheet("VENTAS ");
        Assert.True(EmptyExcelCell(sheet.GetRow(3).GetCell(7)));
        Assert.True(EmptyExcelCell(sheet.GetRow(3).GetCell(4)));
        var evaluator = workbook.GetCreationHelper().CreateFormulaEvaluator();
        var subtotal = evaluator.Evaluate(sheet.GetRow(1).GetCell(7));
        Assert.True(subtotal is null || subtotal.CellType == CellType.Blank
            || subtotal.CellType == CellType.String && subtotal.StringValue.Length == 0);
        var fulfillment = workbook.GetSheet("CUMPLIMIENTO");
        Assert.NotNull(fulfillment);
        var dependentFormulas = fulfillment.Cast<IRow>().SelectMany(r => r.Cells)
            .Where(c => c.CellType == CellType.Formula && c.CellFormula.Contains("VENTAS", StringComparison.OrdinalIgnoreCase)).ToArray();
        Assert.NotEmpty(dependentFormulas);
        Assert.All(dependentFormulas, cell =>
        {
            var result = evaluator.Evaluate(cell);
            Assert.True(result is null || result.CellType == CellType.Blank
                || result.CellType == CellType.String && result.StringValue.Length == 0,
                $"A dependent formula presented a determined total: {cell.CellFormula}");
        });
    }

    [Fact]
    public async Task Prepared_report_edits_preview_without_saving_preserve_snapshot_and_use_concurrency_and_restore()
    {
        await using var factory = new PortalApiFactory(); var admin = await SeedSuperadminAsync(factory);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await LoginAsync(client, admin.Document);
        var historyIds = await SeedPreparedOpsAsync(factory, PreparedOpData("5100"), PreparedOpData("5101"));
        var report = await CreatePreparedReportAsync(client, PreparedManagerFile(new("5100", "A"), new("5100", "B")));
        var originalDetails = JsonSerializer.Serialize(report.Data.Details, ReportTestJson);
        var path = $"/api/commercial/reports/{report.Id}";
        var originalOp = Assert.Single(report.Groups).Op;
        var key = report.Groups[0].Key;
        var request = new { version = report.Version, name = "Reporte ajustado", rowEdits = new[]
            { new { key, factura = "Texto escrito por auxiliar", amount = 125m, setAmount = true, detail = "A corregido\nB" } } };
        var previewResponse = await SendWithCsrfAsync(client, HttpMethod.Post, path + "/prepared/preview", request);
        Assert.True(previewResponse.IsSuccessStatusCode, await previewResponse.Content.ReadAsStringAsync());
        var preview = (await previewResponse.Content.ReadFromJsonAsync<ReportDto>())!;
        Assert.Equal(125, Assert.Single(preview.Groups).Amount);
        Assert.Equal("Texto escrito por auxiliar", preview.Groups[0].Factura);
        var stillSaved = (await client.GetFromJsonAsync<ReportDto>(path))!;
        Assert.Equal(report.Version, stillSaved.Version);
        Assert.Equal(100, stillSaved.Groups[0].Amount);
        Assert.Equal("F01", stillSaved.Groups[0].Factura);

        var historical = (await client.GetFromJsonAsync<OpRecordDto[]>("/api/commercial/reports/ops"))!.Single(r => r.Id == historyIds[0]);
        var changedData = PreparedOpData("5100", "Referencia cambiada después de preparar");
        var changedHistoryResponse = await SendWithCsrfAsync(client, HttpMethod.Put,
            $"/api/commercial/reports/ops/{historical.Id}", new { data = changedData, expectedCapturedAt = historical.CapturedAt });
        Assert.True(changedHistoryResponse.IsSuccessStatusCode, await changedHistoryResponse.Content.ReadAsStringAsync());
        Assert.Equal(originalOp, (await client.GetFromJsonAsync<ReportDto>(path))!.Groups[0].Op);
        var historySnapshotBefore = Preparation(stillSaved).GetProperty("history").GetRawText();
        Assert.Equal(historySnapshotBefore, Preparation((await client.GetFromJsonAsync<ReportDto>(path))!).GetProperty("history").GetRawText());

        var saveResponse = await SendWithCsrfAsync(client, HttpMethod.Put, path + "/prepared", request);
        Assert.True(saveResponse.IsSuccessStatusCode, await saveResponse.Content.ReadAsStringAsync());
        var saved = (await saveResponse.Content.ReadFromJsonAsync<ReportDto>())!;
        Assert.Equal(report.Version + 1, saved.Version);
        Assert.Equal("Reporte ajustado", saved.Name);
        Assert.Equal(125, saved.Groups[0].Amount);
        Assert.Equal("A corregido\nB", string.Join('\n', saved.Groups[0].Details));
        Assert.Equal(originalDetails, JsonSerializer.Serialize(saved.Data.Details, ReportTestJson));
        Assert.NotEmpty(Preparation(saved).GetProperty("rowEdits").EnumerateArray());
        Assert.Equal(HttpStatusCode.Conflict, (await SendWithCsrfAsync(client, HttpMethod.Put, path + "/prepared", request)).StatusCode);
        var export = await SendWithCsrfAsync(client, HttpMethod.Post, path + "/export/draft", new { version = saved.Version });
        Assert.True(export.IsSuccessStatusCode, await export.Content.ReadAsStringAsync());
        using (var workbook = new XSSFWorkbook(new MemoryStream(await export.Content.ReadAsByteArrayAsync())))
        {
            Assert.Equal(saved.Groups[0].Factura, workbook.GetSheet("VENTAS ").GetRow(3).GetCell(2).StringCellValue);
            Assert.Equal(125, workbook.GetSheet("VENTAS ").GetRow(3).GetCell(7).NumericCellValue);
            Assert.Equal("A corregido\nB", workbook.GetSheet("VENTAS ").GetRow(3).GetCell(8).StringCellValue);
        }
        var restoredResponse = await SendWithCsrfAsync(client, HttpMethod.Put, path + "/prepared", new
        { version = saved.Version, name = saved.Name, rowEdits = new[] { new { key, restore = true } } });
        Assert.True(restoredResponse.IsSuccessStatusCode, await restoredResponse.Content.ReadAsStringAsync());
        var restored = (await restoredResponse.Content.ReadFromJsonAsync<ReportDto>())!;
        Assert.Equal(originalOp, restored.Groups[0].Op);
        Assert.Equal(100, restored.Groups[0].Amount);
        Assert.Equal("F01", restored.Groups[0].Factura);
        Assert.Equal(new[] { "A", "B" }, restored.Groups[0].Details);
        Assert.Equal(historySnapshotBefore, Preparation(restored).GetProperty("history").GetRawText());
    }

    [Fact]
    public async Task Consecutive_prepared_row_deltas_preserve_previous_edits_without_replaying_changes()
    {
        await using var factory = new PortalApiFactory(); var admin = await SeedSuperadminAsync(factory);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await LoginAsync(client, admin.Document);
        var report = await CreatePreparedReportAsync(client, PreparedManagerFile(
            new("5150", "Primera fila"), new("5151/5152", "Segunda fila", Amount: 75)));
        var path = $"/api/commercial/reports/{report.Id}";
        var firstKey = report.Groups[0].Key; var secondKey = report.Groups[1].Key;

        async Task SaveDelta(object delta)
        {
            var response = await SendWithCsrfAsync(client, HttpMethod.Put, path + "/prepared",
                new { version = report.Version, name = report.Name, rowEdits = new[] { delta } });
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            report = (await response.Content.ReadFromJsonAsync<ReportDto>())!;
        }
        await SaveDelta(new { key = firstKey, factura = "Manual A" });
        await SaveDelta(new { key = firstKey, client = "Cliente ajustado" });
        await SaveDelta(new { key = secondKey, amount = 80m, setAmount = true });
        var manualChanges = Preparation(report).GetProperty("changes").EnumerateArray()
            .Count(change => change.GetProperty("manual").GetBoolean());
        Assert.Equal(3, manualChanges);
        await SaveDelta(new { key = secondKey, amount = 80m, setAmount = true });
        Assert.Equal(manualChanges, Preparation(report).GetProperty("changes").EnumerateArray()
            .Count(change => change.GetProperty("manual").GetBoolean()));

        var reopened = (await client.GetFromJsonAsync<ReportDto>(path))!;
        Assert.Equal(report.Version, reopened.Version);
        Assert.Equal("Manual A", reopened.Groups[0].Factura);
        Assert.Equal("Cliente ajustado", reopened.Groups[0].Client);
        Assert.Equal(100, reopened.Groups[0].Amount);
        Assert.Equal(80, reopened.Groups[1].Amount);
        var patches = Preparation(reopened).GetProperty("rowEdits").EnumerateArray().ToArray();
        Assert.Equal(2, patches.Length);
        var firstPatch = Assert.Single(patches, patch => patch.GetProperty("key").GetString() == firstKey);
        Assert.Equal("Manual A", firstPatch.GetProperty("factura").GetString());
        Assert.Equal("Cliente ajustado", firstPatch.GetProperty("client").GetString());

        await SaveDelta(new { key = firstKey, restore = true });
        Assert.Equal("F01", report.Groups[0].Factura);
        Assert.Equal("Cliente de ventas", report.Groups[0].Client);
        Assert.Equal(80, report.Groups[1].Amount);
        Assert.Single(Preparation(report).GetProperty("rowEdits").EnumerateArray());
    }

    [Theory]
    [InlineData("00017")]
    [InlineData("  Texto libre de la auxiliar  ")]
    [InlineData("")]
    public async Task New_invoice_default_remains_editable_and_saved_text_or_blank_survives_other_row_edits_and_download(string invoice)
    {
        await using var factory = new PortalApiFactory(); var admin = await SeedSuperadminAsync(factory);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await LoginAsync(client, admin.Document);
        var report = await CreatePreparedReportAsync(client, PreparedManagerFile(new PreparedSourceRow("5160", "OP única")));
        var path = $"/api/commercial/reports/{report.Id}";
        var key = Assert.Single(report.Groups).Key;
        Assert.Equal("F01", report.Groups[0].Factura);
        Assert.Equal("F01", Assert.Single(report.Data.Preparation!.AutomaticRows).Factura);

        async Task SaveDelta(object delta)
        {
            var response = await SendWithCsrfAsync(client, HttpMethod.Put, path + "/prepared",
                new { version = report.Version, name = report.Name, rowEdits = new[] { delta } });
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            report = (await response.Content.ReadFromJsonAsync<ReportDto>())!;
        }
        await SaveDelta(new { key, factura = invoice });
        await SaveDelta(new { key, op = "5161" });
        await SaveDelta(new { key, client = "Cliente corregido" });
        var reopened = (await client.GetFromJsonAsync<ReportDto>(path))!;
        Assert.Equal(invoice, Assert.Single(reopened.Groups).Factura);
        Assert.Equal("5161", reopened.Groups[0].Op);
        Assert.Equal("Cliente corregido", reopened.Groups[0].Client);
        Assert.Equal(invoice, Assert.Single(reopened.Data.Preparation!.RowEdits).Factura);
        Assert.Equal("F01", reopened.Data.Preparation.AutomaticRows[0].Factura);

        var draft = await SendWithCsrfAsync(client, HttpMethod.Post, path + "/export/draft", new { version = reopened.Version });
        Assert.True(draft.IsSuccessStatusCode, await draft.Content.ReadAsStringAsync());
        using (var workbook = new XSSFWorkbook(new MemoryStream(await draft.Content.ReadAsByteArrayAsync())))
        {
            var cell = workbook.GetSheet("VENTAS ").GetRow(3).GetCell(2);
            if (invoice.Length == 0) Assert.True(EmptyExcelCell(cell));
            else Assert.Equal(invoice, cell.StringCellValue);
        }
        await SaveDelta(new { key, restore = true });
        Assert.Equal("F01", report.Groups[0].Factura);
        Assert.Equal("5160", report.Groups[0].Op);
        Assert.Equal("Cliente de ventas", report.Groups[0].Client);
        Assert.Empty(report.Data.Preparation!.RowEdits);
        Assert.Equal("F01", (await client.GetFromJsonAsync<ReportDto>(path))!.Groups[0].Factura);
    }

    [Fact]
    public async Task A_source_used_by_a_legacy_report_prepares_with_new_rules_and_retries_do_not_duplicate()
    {
        await using var factory = new PortalApiFactory(); var admin = await SeedSuperadminAsync(factory);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await LoginAsync(client, admin.Document);
        var bytes = PreparedManagerFile(new PreparedSourceRow("5200", "OP única"));
        var legacyId = await SeedLegacyReportAsync(factory, admin.Id, bytes);
        var legacyBefore = (await client.GetFromJsonAsync<ReportDto>($"/api/commercial/reports/{legacyId}"))!;
        Assert.False(legacyBefore.CanExport);
        var prepared = await CreatePreparedReportAsync(client, bytes);
        Assert.NotEqual(legacyId, prepared.Id);
        Assert.Equal("5200", Assert.Single(prepared.Groups).Op);
        var retried = await CreatePreparedReportAsync(client, bytes);
        Assert.Equal(prepared.Id, retried.Id);
        Assert.Equal(prepared.Version, retried.Version);
        var legacyAfter = (await client.GetFromJsonAsync<ReportDto>($"/api/commercial/reports/{legacyId}"))!;
        Assert.Equal(JsonSerializer.Serialize(legacyBefore, ReportTestJson), JsonSerializer.Serialize(legacyAfter, ReportTestJson));
        Assert.Equal(HttpStatusCode.Conflict, (await SendWithCsrfAsync(client, HttpMethod.Put,
            $"/api/commercial/reports/{legacyId}/prepared", new { version = legacyAfter.Version, name = legacyAfter.Name, rowEdits = Array.Empty<object>() })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await SendWithCsrfAsync(client, HttpMethod.Put,
            $"/api/commercial/reports/{prepared.Id}", Decisions(prepared, Guid.NewGuid()))).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(2, await db.CommercialReports.CountAsync());
    }

    [Fact]
    public async Task Explicit_OP_decisions_select_only_case_candidates_and_keep_original_sales_and_snapshot()
    {
        await using var factory = new PortalApiFactory(); var admin = await SeedSuperadminAsync(factory);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await LoginAsync(client, admin.Document);
        var ids = await SeedPreparedOpsAsync(factory, PreparedOpData("5500", "Referencia A"),
            PreparedOpData("5500", "Referencia B"), PreparedOpData("5501", "Referencia A"), PreparedOpData("5502", "Referencia B"));
        var report = await CreatePreparedReportAsync(client, PreparedManagerFile(new("5500", "Referencia A / A"), new("5500", "Referencia B / B")));
        var path = $"/api/commercial/reports/{report.Id}";
        var sourceBefore = JsonSerializer.Serialize(report.Data.Details, ReportTestJson);
        var request = new { version = report.Version, name = report.Name, rowEdits = Array.Empty<object>(),
            decisions = new[] { OpDecision(report, report.Groups[0].Key, "select_candidates", [ids[0], ids[2]]) } };
        var previewResponse = await SendWithCsrfAsync(client, HttpMethod.Post, path + "/prepared/preview", request);
        Assert.True(previewResponse.IsSuccessStatusCode, await previewResponse.Content.ReadAsStringAsync());
        var preview = (await previewResponse.Content.ReadFromJsonAsync<ReportDto>())!;
        var candidate = Assert.Single(preview.Groups);
        Assert.Contains("5500", candidate.Op); Assert.Contains("5501", candidate.Op); Assert.DoesNotContain("5502", candidate.Op);
        Assert.Equal("N/A", (await client.GetFromJsonAsync<ReportDto>(path))!.Groups[0].Op);
        var savedResponse = await SendWithCsrfAsync(client, HttpMethod.Put, path + "/prepared", request);
        Assert.True(savedResponse.IsSuccessStatusCode, await savedResponse.Content.ReadAsStringAsync());
        var saved = (await savedResponse.Content.ReadFromJsonAsync<ReportDto>())!;
        Assert.Equal(candidate.Op, saved.Groups[0].Op);
        Assert.Equal(sourceBefore, JsonSerializer.Serialize(saved.Data.Details, ReportTestJson));
        Assert.Equal(new[] { "Referencia A / A", "Referencia B / B" }, saved.Groups[0].Details);

        var laterRecord = await SendWithCsrfAsync(client, HttpMethod.Post, "/api/commercial/reports/ops", new { data = PreparedOpData("5599", "Referencia A") });
        Assert.True(laterRecord.IsSuccessStatusCode, await laterRecord.Content.ReadAsStringAsync());
        var newId = (await laterRecord.Content.ReadFromJsonAsync<OpRecordDto>())!.Id;
        var initialCase = Preparation(report).GetProperty("review").GetProperty("cases").EnumerateArray().Single(c => c.GetProperty("rowKey").GetString() == report.Groups[0].Key);
        var outsideSnapshot = await SendWithCsrfAsync(client, HttpMethod.Post, path + "/prepared/preview", new
        { version = saved.Version, name = saved.Name, rowEdits = Array.Empty<object>(), decisions = new[] { new
            { caseId = initialCase.GetProperty("id").GetString(), findingIds = initialCase.GetProperty("findings").EnumerateArray().Where(f => f.GetProperty("field").GetString() == "NUMERO OP").Select(f => f.GetProperty("id").GetString()).ToArray(), action = "select_candidates", historyIds = new[] { newId } } } });
        Assert.Equal(HttpStatusCode.BadRequest, outsideSnapshot.StatusCode);
        Assert.Equal(saved.Groups[0].Op, (await client.GetFromJsonAsync<ReportDto>(path))!.Groups[0].Op);
    }

    [Fact]
    public async Task Keep_NA_preview_is_isolated_and_human_confirmation_allows_approved_final_without_changing_the_cell()
    {
        await using var factory = new PortalApiFactory(); var admin = await SeedSuperadminAsync(factory);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await LoginAsync(client, admin.Document);
        var bytes = PreparedManagerFile(new PreparedSourceRow("", "Producto / detalle"));
        var report = await CreatePreparedReportAsync(client, bytes);
        var path = $"/api/commercial/reports/{report.Id}";
        Assert.Equal("N/A", Assert.Single(report.Groups).Op);
        Assert.Equal(1, report.Data.Preparation!.Review!.Summary.ValidationRows);
        var decision = OpDecision(report, report.Groups[0].Key, "keep_na");
        var request = new { version = report.Version, name = report.Name, rowEdits = Array.Empty<object>(), decisions = new[] { decision } };
        var previewResponse = await SendWithCsrfAsync(client, HttpMethod.Post, path + "/prepared/preview", request);
        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        var preview = (await previewResponse.Content.ReadFromJsonAsync<ReportDto>())!;
        Assert.Equal(0, preview.Data.Preparation!.Review!.Summary.PendingCases);
        var unchanged = (await client.GetFromJsonAsync<ReportDto>(path))!;
        Assert.Equal(1, unchanged.Data.Preparation!.Review!.Summary.PendingCases);
        Assert.Empty(unchanged.Data.Preparation.Review.Decisions);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.False(await db.AuditEvents.AnyAsync(e => e.EntityId == report.Id.ToString() && e.Action == "commercial.report.decided"));
        }
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await SendWithCsrfAsync(client, HttpMethod.Post, path + "/approve", new { version = report.Version })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await SendWithCsrfAsync(client, HttpMethod.Post, path + "/export", new { version = report.Version })).StatusCode);

        report = await SavePreparedDecisionsAsync(client, report, decision);
        var savedDecision = Assert.Single(report.Data.Preparation!.Review!.Decisions);
        Assert.Equal("N/A", savedDecision.Before); Assert.Equal("N/A", savedDecision.After);
        Assert.Equal("keep_na", savedDecision.Action); Assert.Equal(admin.Id.ToString(), savedDecision.Actor);
        Assert.Equal(report.Version, savedDecision.ReportVersion); Assert.NotEqual(default, savedDecision.OccurredAt);
        Assert.DoesNotContain(report.Data.Preparation.Changes, change => change.Manual && change.Field == "NUMERO OP");
        Assert.Equal(1, report.Data.Preparation.Review.Summary.HumanResolvedRows);
        Assert.Equal(0, report.Data.Preparation.Review.Summary.PendingCases);
        Assert.True(report.CanApprove); Assert.False(report.CanExportFinal);
        Assert.Equal(HttpStatusCode.Conflict, (await SendWithCsrfAsync(client, HttpMethod.Put, path + "/prepared", request)).StatusCode);
        var retry = await CreatePreparedReportAsync(client, bytes);
        Assert.Equal(report.Id, retry.Id); Assert.Equal(report.Version, retry.Version);
        Assert.Equal(JsonSerializer.Serialize(report.Data.Preparation.Review, ReportTestJson), JsonSerializer.Serialize(retry.Data.Preparation!.Review, ReportTestJson));

        var draft = await SendWithCsrfAsync(client, HttpMethod.Post, path + "/export/draft", new { version = report.Version });
        Assert.Equal(HttpStatusCode.OK, draft.StatusCode);
        Assert.Contains("BORRADOR", draft.Content.Headers.ContentDisposition!.FileNameStar ?? draft.Content.Headers.ContentDisposition.FileName);
        using (var book = new XSSFWorkbook(new MemoryStream(await draft.Content.ReadAsByteArrayAsync())))
        {
            Assert.Contains("BORRADOR", book.GetSheet("VENTAS ").GetRow(0).GetCell(0).StringCellValue);
            Assert.Contains("BORRADOR", book.GetSheet("VENTAS ").Header.Center);
            Assert.Equal("N/A", book.GetSheet("VENTAS ").GetRow(3).GetCell(1).StringCellValue);
        }
        Assert.Null((await client.GetFromJsonAsync<ReportDto>(path))!.LastExportedVersion);

        report = await ApprovePreparedAsync(client, report);
        Assert.True(report.CanExportFinal);
        var approvedVersion = report.Version;
        for (var i = 0; i < 2; i++)
        {
            var final = await SendWithCsrfAsync(client, HttpMethod.Post, path + "/export", new { version = approvedVersion });
            Assert.Equal(HttpStatusCode.OK, final.StatusCode);
            using var book = new XSSFWorkbook(new MemoryStream(await final.Content.ReadAsByteArrayAsync()));
            Assert.Equal("N/A", book.GetSheet("VENTAS ").GetRow(3).GetCell(1).StringCellValue);
            Assert.True(EmptyExcelCell(book.GetSheet("VENTAS ").GetRow(0)?.GetCell(0)));
        }
        report = (await client.GetFromJsonAsync<ReportDto>(path))!;
        Assert.Equal(approvedVersion, report.Version); Assert.Equal(approvedVersion, report.LastExportedVersion);
        Assert.True(report.CanExportFinal);
        var changed = await SendWithCsrfAsync(client, HttpMethod.Put, path + "/prepared", new
        { version = report.Version, name = report.Name, rowEdits = new[] { new { key = report.Groups[0].Key, factura = "00017" } } });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        report = (await changed.Content.ReadFromJsonAsync<ReportDto>())!;
        Assert.Equal("00017", report.Groups[0].Factura);
        Assert.Equal(1, report.Data.Preparation!.Review!.Summary.HumanResolvedRows);
        Assert.Equal(0, report.Data.Preparation.Review.Summary.PendingCases);
        Assert.False(report.CanExportFinal); Assert.True(report.CanApprove);
        Assert.False(Assert.Single(report.Data.Preparation.Review.Approvals).Valid);
        Assert.Equal(HttpStatusCode.Conflict, (await SendWithCsrfAsync(client, HttpMethod.Post, path + "/export", new { version = approvedVersion })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await SendWithCsrfAsync(client, HttpMethod.Post, path + "/export", new { version = report.Version })).StatusCode);
        report = await ApprovePreparedAsync(client, report);
        Assert.True(report.CanExportFinal);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var events = await db.AuditEvents.Where(e => e.EntityId == report.Id.ToString()).Select(e => e.Action).ToArrayAsync();
            Assert.Contains("commercial.report.decided", events); Assert.Contains("commercial.report.approved", events);
            Assert.Contains("commercial.report.approval_invalidated", events); Assert.Contains("commercial.report.exported_draft", events);
            Assert.Contains("commercial.report.exported_final", events);
        }
    }

    [Fact]
    public async Task Invalid_or_contradictory_decisions_do_not_save_any_row_patch_or_resolution()
    {
        await using var factory = new PortalApiFactory(); var admin = await SeedSuperadminAsync(factory);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await LoginAsync(client, admin.Document);
        var ids = await SeedPreparedOpsAsync(factory, PreparedOpData("6100", "Producto A"), PreparedOpData("6101", "Producto B"));
        var report = await CreatePreparedReportAsync(client, PreparedManagerFile(new("", "Producto A / A"), new("", "Producto B / B")));
        var path = $"/api/commercial/reports/{report.Id}";
        var key = report.Groups[0].Key;
        foreach (var decisions in new[]
        {
            new[] { OpDecision(report, key, "select_candidates", [ids[1]]) },
            new[] { OpDecision(report, key, "keep_na"), OpDecision(report, key, "set_manual_op", op: "7000") },
            new[] { OpDecision(report, key, "keep_na", op: "7000") },
            new[] { OpDecision(report, key, "set_manual_op", op: new string('x', 32768)) }
        })
        {
            var invalid = await SendWithCsrfAsync(client, HttpMethod.Put, path + "/prepared", new
            { version = report.Version, name = "No guardar", rowEdits = new[] { new { key, factura = "NO GUARDAR" } }, decisions });
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            var unchanged = (await client.GetFromJsonAsync<ReportDto>(path))!;
            Assert.Equal(JsonSerializer.Serialize(report, ReportTestJson), JsonSerializer.Serialize(unchanged, ReportTestJson));
        }
        report = await SavePreparedDecisionsAsync(client, report, OpDecision(report, key, "set_manual_op", op: "OP escrita 0005"));
        Assert.Equal("OP escrita 0005", report.Groups[0].Op);
        Assert.Empty(report.Data.Preparation!.Review!.Decisions.Single().HistoryIds);
        Assert.Equal(1, report.Data.Preparation.Review.Summary.ValidationRows);
        Assert.Equal(1, report.Data.Preparation.Review.Summary.HumanResolvedRows);
    }

    [Fact]
    public async Task Resolving_OP_does_not_accept_amount_conflicts_or_file_findings()
    {
        await using var factory = new PortalApiFactory(); var admin = await SeedSuperadminAsync(factory);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await LoginAsync(client, admin.Document);
        var report = await CreatePreparedReportAsync(client, PreparedManagerFile(new("6200", "A", Amount: 100), new("6200", "B", Amount: 200)));
        report = await SavePreparedDecisionsAsync(client, report, OpDecision(report, report.Groups[0].Key, "keep_na"));
        Assert.Null(report.Groups[0].Amount);
        Assert.Equal(1, report.Data.Preparation!.Review!.Summary.ConflictRows);
        Assert.Equal(0, report.Data.Preparation.Review.Summary.HumanResolvedRows);
        var path = $"/api/commercial/reports/{report.Id}";
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await SendWithCsrfAsync(client, HttpMethod.Post, path + "/approve", new { version = report.Version })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendWithCsrfAsync(client, HttpMethod.Post, path + "/export/draft", new { version = report.Version })).StatusCode);

        using var source = new XSSFWorkbook(new MemoryStream(PreparedManagerFile(new PreparedSourceRow("6300", "OP única"))));
        source.GetSheetAt(0).CreateRow(2).CreateCell(0).SetCellValue("Pie de informe");
        using var output = new MemoryStream(); source.Write(output, true);
        var fileReport = await CreatePreparedReportAsync(client, output.ToArray());
        Assert.Equal(1, fileReport.Data.Preparation!.Review!.Summary.FilePendingCases);
        Assert.False(fileReport.CanApprove);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await SendWithCsrfAsync(client, HttpMethod.Post, $"/api/commercial/reports/{fileReport.Id}/approve", new { version = fileReport.Version })).StatusCode);
    }

    [Fact]
    public async Task Approval_and_draft_endpoints_enforce_permissions_ownership_csrf_and_actual_capabilities()
    {
        await using var factory = new PortalApiFactory(); var admin = await SeedSuperadminAsync(factory);
        var reader = await CreatePermissionedUserAsync(factory, "Lectura de reportes", [ReportPermissionCodes.View, ReportPermissionCodes.Export]);
        var agent = await CreateUserInExistingRoleAsync(factory, DatabaseSeeder.CommercialAgentRoleName, "Agente");
        using var ownerClient = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        using var readerClient = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        using var agentClient = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await LoginAsync(ownerClient, admin.Document); await LoginAsync(readerClient, reader); await LoginAsync(agentClient, agent);
        var report = await CreatePreparedReportAsync(ownerClient, PreparedManagerFile(new PreparedSourceRow("6400", "OP única")));
        var path = $"/api/commercial/reports/{report.Id}";
        Assert.True(report.CanApprove); Assert.False(report.CanExportFinal);
        Assert.Equal(HttpStatusCode.BadRequest, (await ownerClient.PostAsJsonAsync(path + "/approve", new { version = report.Version })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ownerClient.PostAsJsonAsync(path + "/export/draft", new { version = report.Version })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendWithCsrfAsync(readerClient, HttpMethod.Post, path + "/approve", new { version = report.Version })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendWithCsrfAsync(agentClient, HttpMethod.Post, path + "/export/draft", new { version = report.Version })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendWithCsrfAsync(readerClient, HttpMethod.Post, path + "/export/draft", new { version = report.Version })).StatusCode);
        report = await ApprovePreparedAsync(ownerClient, report);
        var obsolete = report.Version - 1;
        Assert.Equal(HttpStatusCode.Conflict, (await SendWithCsrfAsync(ownerClient, HttpMethod.Post, path + "/approve", new { version = obsolete })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await SendWithCsrfAsync(ownerClient, HttpMethod.Post, path + "/export/draft", new { version = obsolete })).StatusCode);

        // Change ownership in the isolated test database to inspect a reader's
        // effective capabilities without granting a new approval role.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var readerUser = await db.Users.SingleAsync(u => u.UserName == reader);
            (await db.CommercialReports.SingleAsync(r => r.Id == report.Id)).OwnerUserId = readerUser.Id;
            await db.SaveChangesAsync();
        }
        var readOnly = (await readerClient.GetFromJsonAsync<ReportDto>(path))!;
        Assert.False(readOnly.CanApprove); Assert.True(readOnly.CanExportDraft); Assert.True(readOnly.CanExportFinal);
    }

    [Fact]
    public async Task Saving_no_effective_change_preserves_approval_but_a_renamed_revision_requires_its_own_approval()
    {
        await using var factory = new PortalApiFactory(); var admin = await SeedSuperadminAsync(factory);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await LoginAsync(client, admin.Document);
        var report = await CreatePreparedReportAsync(client, PreparedManagerFile(new PreparedSourceRow("6450", "OP única")));
        report = await ApprovePreparedAsync(client, report);
        var path = $"/api/commercial/reports/{report.Id}/prepared";
        var noChange = await SendWithCsrfAsync(client, HttpMethod.Put, path, new
        { version = report.Version, name = report.Name, rowEdits = new[] { new { key = report.Groups[0].Key, factura = report.Groups[0].Factura } } });
        Assert.Equal(HttpStatusCode.OK, noChange.StatusCode);
        var unchanged = (await noChange.Content.ReadFromJsonAsync<ReportDto>())!;
        Assert.Equal(JsonSerializer.Serialize(report, ReportTestJson), JsonSerializer.Serialize(unchanged, ReportTestJson));
        Assert.True(unchanged.CanExportFinal);
        var renamed = await SendWithCsrfAsync(client, HttpMethod.Put, path, new
        { version = report.Version, name = "Nombre nuevo", rowEdits = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        var changed = (await renamed.Content.ReadFromJsonAsync<ReportDto>())!;
        Assert.Equal(report.Version + 1, changed.Version); Assert.Equal("Nombre nuevo", changed.Name);
        Assert.False(changed.CanExportFinal); Assert.True(changed.CanApprove);
        Assert.False(Assert.Single(changed.Data.Preparation!.Review!.Approvals).Valid);
        Assert.Equal(report.Version, changed.Data.Preparation.Review.Approvals.Single().ReportVersion);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.AuditEvents.CountAsync(e => e.EntityId == report.Id.ToString() && e.Action == "commercial.report.prepared_edited"));
        Assert.Equal(1, await db.AuditEvents.CountAsync(e => e.EntityId == report.Id.ToString() && e.Action == "commercial.report.approval_invalidated"));
    }

    [Fact]
    public async Task V2_snapshots_remain_editable_as_drafts_and_new_policy_reuses_source_without_reinterpreting_them()
    {
        await using var factory = new PortalApiFactory(); var admin = await SeedSuperadminAsync(factory);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await LoginAsync(client, admin.Document);
        var bytes = PreparedManagerFile(new PreparedSourceRow("6500", "OP original"));
        var created = await CreatePreparedReportAsync(client, bytes);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var old = await db.CommercialReports.SingleAsync(r => r.Id == created.Id);
            var data = JsonSerializer.Deserialize<SalesReportData>(old.DataJson, ReportTestJson)!;
            data.Preparation!.RuleVersion = 2; data.Preparation.Review = null;
            data.Preparation.IdentityKey = "prepared-v2-preserved";
            old.DataJson = JsonSerializer.Serialize(data, ReportTestJson);
            await db.SaveChangesAsync();
        }
        var path = $"/api/commercial/reports/{created.Id}";
        var previous = (await client.GetFromJsonAsync<ReportDto>(path))!;
        Assert.Null(previous.Data.Preparation!.Review); Assert.False(previous.CanApprove); Assert.False(previous.CanExportFinal);
        var edit = await SendWithCsrfAsync(client, HttpMethod.Put, path + "/prepared", new
        { version = previous.Version, name = previous.Name, rowEdits = new[] { new { key = previous.Groups[0].Key, factura = "00042" } } });
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        previous = (await edit.Content.ReadFromJsonAsync<ReportDto>())!;
        Assert.Equal("00042", previous.Groups[0].Factura); Assert.Null(previous.Data.Preparation!.Review);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await SendWithCsrfAsync(client, HttpMethod.Post, path + "/export", new { version = previous.Version })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await SendWithCsrfAsync(client, HttpMethod.Post, path + "/approve", new { version = previous.Version })).StatusCode);
        var draft = await SendWithCsrfAsync(client, HttpMethod.Post, path + "/export/draft", new { version = previous.Version });
        Assert.Equal(HttpStatusCode.OK, draft.StatusCode);
        using (var book = new XSSFWorkbook(new MemoryStream(await draft.Content.ReadAsByteArrayAsync())))
            Assert.Contains("VERSIÓN ANTERIOR", book.GetSheet("VENTAS ").GetRow(0).GetCell(0).StringCellValue);
        var next = await CreatePreparedReportAsync(client, bytes);
        Assert.NotEqual(previous.Id, next.Id); Assert.NotNull(next.Data.Preparation!.Review);
        Assert.Equal("F01", next.Groups[0].Factura);
        Assert.Equal(JsonSerializer.Serialize(previous, ReportTestJson), JsonSerializer.Serialize((await client.GetFromJsonAsync<ReportDto>(path))!, ReportTestJson));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(1, await db.CommercialReportSources.CountAsync()); Assert.Equal(2, await db.CommercialReports.CountAsync());
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("Factura guardada en V3")]
    public async Task V3_invoice_defaults_complete_without_repreparing_and_explicit_values_survive_an_idempotent_V4_preparation(string savedInvoice)
    {
        await using var factory = new PortalApiFactory(); var admin = await SeedSuperadminAsync(factory);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await LoginAsync(client, admin.Document);
        var bytes = PreparedManagerFile(new PreparedSourceRow("6510", "OP única"));
        var created = await CreatePreparedReportAsync(client, bytes);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var saved = await db.CommercialReports.SingleAsync(report => report.Id == created.Id);
            var data = JsonSerializer.Deserialize<SalesReportData>(saved.DataJson, ReportTestJson)!;
            var preparation = data.Preparation!;
            var original = Assert.Single(preparation.Rows);
            var detail = Assert.Single(data.Details);
            // Seed the materialized shape and identity of a saved V3 report.
            // Its prior blank default receives F01; explicit invoice edits survive.
            var oldKey = SalesReportEngine.Key("prepared-v3", SalesReportEngine.Key("sale-number-line",
                ReportPreparationEngine.Identity(detail.Number), ReportPreparationEngine.Identity(detail.ManagerOp), ReportPreparationEngine.Identity(detail.Line)));
            preparation.RuleVersion = 3;
            preparation.IdentityKey = SalesReportEngine.Key("prepared-v3", "normalization-v1", "review-v1", data.Sha256, preparation.HistoryFingerprint);
            preparation.Rows[0] = original with { Key = oldKey, Factura = savedInvoice, Modified = savedInvoice.Length > 0 };
            preparation.AutomaticRows[0] = preparation.AutomaticRows[0] with { Key = oldKey, Factura = "" };
            if (savedInvoice.Length > 0) preparation.RowEdits = [new() { Key = oldKey, Factura = savedInvoice }];
            foreach (var change in preparation.Changes.Where(change => change.RowKey == original.Key)) change.RowKey = oldKey;
            foreach (var reviewCase in preparation.Review!.Cases.Where(reviewCase => reviewCase.RowKey == original.Key))
            {
                reviewCase.RowKey = oldKey;
                reviewCase.Id = SalesReportEngine.Key("review-row-v1", oldKey);
            }
            saved.DataJson = JsonSerializer.Serialize(data, ReportTestJson);
            await db.SaveChangesAsync();
        }
        var path = $"/api/commercial/reports/{created.Id}";
        var previous = (await client.GetFromJsonAsync<ReportDto>(path))!;
        Assert.Equal(3, previous.Data.Preparation!.RuleVersion);
        Assert.Equal(savedInvoice.Length == 0 ? "F01" : savedInvoice, previous.Groups[0].Factura);
        var edit = await SendWithCsrfAsync(client, HttpMethod.Put, path + "/prepared", new
        { version = previous.Version, name = previous.Name, rowEdits = new[] { new { key = previous.Groups[0].Key, client = "Nombre actualizado en V3" } } });
        Assert.True(edit.IsSuccessStatusCode, await edit.Content.ReadAsStringAsync());
        previous = (await edit.Content.ReadFromJsonAsync<ReportDto>())!;
        Assert.Equal(3, previous.Data.Preparation!.RuleVersion);
        Assert.Equal(savedInvoice.Length == 0 ? "F01" : savedInvoice, previous.Groups[0].Factura);
        Assert.Equal("F01", previous.Data.Preparation.AutomaticRows[0].Factura);
        var previousSnapshot = JsonSerializer.Serialize(previous, ReportTestJson);
        string previousSavedJson;
        await using (var scope = factory.Services.CreateAsyncScope())
            previousSavedJson = (await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
                .CommercialReports.AsNoTracking().SingleAsync(report => report.Id == previous.Id)).DataJson;

        var next = await CreatePreparedReportAsync(client, bytes);
        Assert.NotEqual(previous.Id, next.Id);
        Assert.NotEqual(previous.Groups[0].Key, next.Groups[0].Key);
        Assert.NotEqual(previous.Data.Preparation.IdentityKey, next.Data.Preparation!.IdentityKey);
        Assert.Equal("F01", next.Groups[0].Factura);
        var retry = await CreatePreparedReportAsync(client, bytes);
        Assert.Equal(next.Id, retry.Id);
        Assert.Equal(next.Version, retry.Version);
        Assert.Equal(previousSnapshot, JsonSerializer.Serialize((await client.GetFromJsonAsync<ReportDto>(path))!, ReportTestJson));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(previousSavedJson, (await db.CommercialReports.AsNoTracking().SingleAsync(report => report.Id == previous.Id)).DataJson);
            Assert.Equal(1, await db.CommercialReportSources.CountAsync());
            Assert.Equal(2, await db.CommercialReports.CountAsync());
        }
    }

    [Fact]
    public async Task Prepared_edits_validate_only_changed_fields_and_reject_unknown_rows_and_unrepresentable_Excel_text()
    {
        await using var factory = new PortalApiFactory(); var admin = await SeedSuperadminAsync(factory);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await LoginAsync(client, admin.Document);
        var report = await CreatePreparedReportAsync(client, PreparedManagerFile(new PreparedSourceRow("", "Sin número")));
        var path = $"/api/commercial/reports/{report.Id}/prepared/preview";
        var key = report.Groups[0].Key;
        var valid = await SendWithCsrfAsync(client, HttpMethod.Post, path, new
        { version = report.Version, name = report.Name, rowEdits = new[] { new { key, factura = "Texto manual opcional" } } });
        Assert.True(valid.IsSuccessStatusCode, await valid.Content.ReadAsStringAsync());
        foreach (var patch in new object[]
        {
            new { key = "fila inexistente", factura = "Sin efecto" },
            new { key, date = "2026-02-31" },
            new { key, amount = 1.234m, setAmount = true },
            new { key, detail = new string('x', 32768) }
        })
        {
            var invalid = await SendWithCsrfAsync(client, HttpMethod.Post, path,
                new { version = report.Version, name = report.Name, rowEdits = new[] { patch } });
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        Assert.Equal(report.Version, (await client.GetFromJsonAsync<ReportDto>($"/api/commercial/reports/{report.Id}"))!.Version);
    }

    [Fact]
    public async Task A_preparation_that_exceeds_Excel_cell_capacity_does_not_publish_a_partial_report()
    {
        await using var factory = new PortalApiFactory(); var admin = await SeedSuperadminAsync(factory);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await LoginAsync(client, admin.Document);
        var tooLongAfterCombining = PreparedManagerFile(new("5600", new string('a', 16384)), new("5600", new string('b', 16384)));
        var response = await ReportFile(client, "/api/commercial/reports", tooLongAfterCombining);
        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.UnprocessableEntity });
        Assert.Empty((await client.GetFromJsonAsync<ReportSummary[]>("/api/commercial/reports"))!);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(0, await db.CommercialReports.CountAsync());
        Assert.Equal(0, await db.CommercialReportSources.CountAsync());
    }

    [Fact]
    public async Task Historical_imports_preserve_row_multiplicity_and_updated_copies_only_add_new_rows()
    {
        await using var factory = new PortalApiFactory(); var admin = await SeedSuperadminAsync(factory);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await LoginAsync(client, admin.Document);
        var first = PreparedOpData("5300");
        var samePhysicalRows = PreparedOpsFile(first, first);
        var imported = await ReportFile(client, "/api/commercial/reports/ops/import", samePhysicalRows);
        Assert.True(imported.IsSuccessStatusCode, await imported.Content.ReadAsStringAsync());
        Assert.Equal(2, (await imported.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("imported").GetInt32());
        var identical = await ReportFile(client, "/api/commercial/reports/ops/import", samePhysicalRows);
        Assert.Equal(0, (await identical.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("imported").GetInt32());
        var updated = await ReportFile(client, "/api/commercial/reports/ops/import", PreparedOpsFile(first, first, PreparedOpData("5301")));
        Assert.True(updated.IsSuccessStatusCode, await updated.Content.ReadAsStringAsync());
        Assert.Equal(1, (await updated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("imported").GetInt32());
        var records = (await client.GetFromJsonAsync<OpRecordDto[]>("/api/commercial/reports/ops"))!;
        Assert.Equal(3, records.Length); Assert.Equal(2, records.Count(r => r.Number == "5300"));
        var changed = PreparedOpData("5300"); changed.Cells[18] = "2";
        var ambiguousUpdate = await ReportFile(client, "/api/commercial/reports/ops/import", PreparedOpsFile(changed, first, PreparedOpData("5301")));
        Assert.Equal(HttpStatusCode.Conflict, ambiguousUpdate.StatusCode);
        Assert.Equal(3, (await client.GetFromJsonAsync<OpRecordDto[]>("/api/commercial/reports/ops"))!.Length);
    }

    [Fact]
    public async Task Historical_record_manual_save_preserves_previous_version_and_rejects_stale_edits()
    {
        await using var factory = new PortalApiFactory(); var admin = await SeedSuperadminAsync(factory);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await LoginAsync(client, admin.Document);
        var createdResponse = await SendWithCsrfAsync(client, HttpMethod.Post, "/api/commercial/reports/ops", new { data = PreparedOpData("5400") });
        Assert.True(createdResponse.IsSuccessStatusCode, await createdResponse.Content.ReadAsStringAsync());
        var created = (await createdResponse.Content.ReadFromJsonAsync<OpRecordDto>())!;
        var corrected = PreparedOpData("5401", "Referencia completada");
        var payload = new { data = corrected, expectedCapturedAt = created.CapturedAt };
        var path = $"/api/commercial/reports/ops/{created.Id}";
        var savedResponse = await SendWithCsrfAsync(client, HttpMethod.Put, path, payload);
        Assert.True(savedResponse.IsSuccessStatusCode, await savedResponse.Content.ReadAsStringAsync());
        var saved = (await savedResponse.Content.ReadFromJsonAsync<OpRecordDto>())!;
        Assert.NotEqual(created.Id, saved.Id);
        Assert.Equal("5401", saved.Number);
        Assert.Equal("Referencia completada", saved.Data.Cells[6]);
        Assert.Equal(HttpStatusCode.Conflict, (await SendWithCsrfAsync(client, HttpMethod.Put, path, payload)).StatusCode);
        var active = Assert.Single((await client.GetFromJsonAsync<OpRecordDto[]>("/api/commercial/reports/ops"))!);
        Assert.Equal(saved.Id, active.Id);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(2, await db.OpReportRecords.CountAsync());
        Assert.Equal("5400", (await db.OpReportRecords.SingleAsync(r => r.Id == created.Id)).Number);
        Assert.Equal(0, await db.ProductionOrders.CountAsync());
    }

    [Fact]
    public async Task Historical_import_accepts_incomplete_product_and_reference_without_replacing_FG_with_product()
    {
        await using var factory = new PortalApiFactory(); var admin = await SeedSuperadminAsync(factory);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await LoginAsync(client, admin.Document);
        var valid = PreparedOpData("5700", product: "");
        var sibling = PreparedOpData("5701", product: "");
        var incomplete = PreparedOpData("5800", reference: "", historicalClient: "", product: "");
        var import = await ReportFile(client, "/api/commercial/reports/ops/import", PreparedOpsFile(valid, sibling, incomplete));
        Assert.True(import.IsSuccessStatusCode, await import.Content.ReadAsStringAsync());
        Assert.Equal(3, (await import.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("imported").GetInt32());
        var report = await CreatePreparedReportAsync(client, PreparedManagerFile(new("5700", "A"), new("5700", "B"),
            new("5800", "Sin referencia A"), new("5800", "Sin referencia B")));
        var matched = Assert.Single(report.Groups, row => row.DetailIds.Any(id => report.Data.Details.Single(d => d.Id == id).ManagerOp == "5700"));
        Assert.Equal("N/A", matched.Op); Assert.NotEmpty(matched.Issues);
        var provisional = Assert.Single(report.Groups, row => row.DetailIds.Any(id => report.Data.Details.Single(d => d.Id == id).ManagerOp == "5800"));
        Assert.NotEmpty(provisional.Issues);
        Assert.DoesNotContain("5701", provisional.Op);
    }

    [Fact]
    public async Task A_later_portal_dispatch_preserves_manually_completed_historical_fields_and_updates_automatic_fields()
    {
        await using var factory = new PortalApiFactory(disableCommercialOutboxWorker: true);
        var admin = await SeedSuperadminAsync(factory);
        var assistantDocument = await CreateUserInExistingRoleAsync(factory, DatabaseSeeder.CommercialAssistantRoleName, "Auxiliar");
        using var commercial = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        using var assistant = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await LoginAsync(commercial, admin.Document); await LoginAsync(assistant, assistantDocument);
        var draft = await CreateReviewDraftAsync(factory, commercial);
        var orderId = draft.GetProperty("id").GetGuid();
        var reviewResponse = await SendWithCsrfAsync(commercial, HttpMethod.Post,
            $"/api/commercial/production-orders/{orderId}/submit-for-review", new { version = draft.GetProperty("version").GetInt32() });
        Assert.True(reviewResponse.IsSuccessStatusCode, await reviewResponse.Content.ReadAsStringAsync());
        var pending = await reviewResponse.Content.ReadFromJsonAsync<JsonElement>();
        var version = pending.GetProperty("version").GetInt32();
        var reviewerId = pending.GetProperty("reviewOwner").GetProperty("id").GetGuid();
        var dispatchResponse = await SendWithCsrfAsync(assistant, HttpMethod.Post,
            $"/api/commercial/production-orders/{orderId}/submit", new { version, customerOrderNumber = "PED-5900" });
        Assert.True(dispatchResponse.IsSuccessStatusCode, await dispatchResponse.Content.ReadAsStringAsync());
        var originalRecord = Assert.Single((await assistant.GetFromJsonAsync<OpRecordDto[]>("/api/commercial/reports/ops"))!);
        var completedCells = originalRecord.Data.Cells.ToArray();
        completedCells[0] = "5900";
        completedCells[4] = "900123456";
        completedCells[5] = "Nombre confirmado por auxiliar";
        completedCells[6] = "Detalle confirmado por auxiliar";
        var completedResponse = await SendWithCsrfAsync(assistant, HttpMethod.Put,
            $"/api/commercial/reports/ops/{originalRecord.Id}", new
            { data = new OpRecordData { Cells = completedCells, PortalCode = originalRecord.Data.PortalCode }, expectedCapturedAt = originalRecord.CapturedAt });
        Assert.True(completedResponse.IsSuccessStatusCode, await completedResponse.Content.ReadAsStringAsync());
        var completed = (await completedResponse.Content.ReadFromJsonAsync<OpRecordDto>())!;

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var order = await db.ProductionOrders.SingleAsync(o => o.Id == orderId);
            Assert.Equal("PED-5900", order.CustomerOrderNumber);
            Assert.NotEqual(completedCells[5], order.ClientName);
            // The application has no action to reopen an approved OP. This
            // synthetic fixture represents a later dispatch event of the same
            // order; the tested dispatch itself uses the real service endpoint.
            order.Status = ProductionOrderStatus.PendingCommercialReview;
            order.ReviewOwnerUserId = reviewerId;
            order.CurrentAssigneeUserId = reviewerId;
            order.ClientName = "Nuevo nombre en orden de Producción";
            order.ReferenceNumber = "Nueva referencia en orden de Producción";
            order.ProductName = "Producto automático actualizado";
            order.Version++;
            version = order.Version;
            await db.SaveChangesAsync();
        }
        var redispatchedResponse = await SendWithCsrfAsync(assistant, HttpMethod.Post,
            $"/api/commercial/production-orders/{orderId}/submit", new { version, customerOrderNumber = "PED-5901" });
        Assert.True(redispatchedResponse.IsSuccessStatusCode, await redispatchedResponse.Content.ReadAsStringAsync());
        var latest = Assert.Single((await assistant.GetFromJsonAsync<OpRecordDto[]>("/api/commercial/reports/ops"))!);
        Assert.NotEqual(completed.Id, latest.Id);
        Assert.Equal(version + 1, latest.OrderVersion);
        foreach (var index in new[] { 0, 4, 5, 6 }) Assert.Equal(completedCells[index], latest.Data.Cells[index]);
        Assert.Equal("5900", latest.Number);
        Assert.Equal(completedCells[5], latest.Client);
        Assert.Equal("Producto automático actualizado", latest.Product);
        Assert.Equal("Producto automático actualizado", latest.Data.Cells[9]);
        Assert.Equal(originalRecord.Code, latest.Code);
        await using var finalScope = factory.Services.CreateAsyncScope();
        var finalDb = finalScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var registryVersions = (await finalDb.OpReportRecords.ToArrayAsync())
            .Select(record => (Record: record, Data: JsonSerializer.Deserialize<OpRecordData>(record.DataJson)!))
            .Where(item => item.Data.RegistryId == orderId)
            .OrderBy(item => item.Data.RegistryVersion).ToArray();
        Assert.Equal(3, registryVersions.Length);
        Assert.Equal(new[] { 1, 2, 3 }, registryVersions.Select(item => item.Data.RegistryVersion));
        Assert.Equal(2, registryVersions.Count(item => item.Record.ProductionOrderId == orderId));
        Assert.Null(registryVersions[1].Record.ProductionOrderId); // Manual revision, not another Portal capture.
        Assert.Null(registryVersions[0].Data.PreviousRecordId);
        Assert.Equal(originalRecord.Id, registryVersions[1].Data.PreviousRecordId);
        Assert.Equal(completed.Id, registryVersions[2].Data.PreviousRecordId);
        Assert.Equal(completed.Id, registryVersions[0].Data.SupersededById);
        Assert.Equal(latest.Id, registryVersions[1].Data.SupersededById);
        Assert.Null(registryVersions[2].Data.SupersededById);
        Assert.Equal("PED-5901", (await finalDb.ProductionOrders.SingleAsync(o => o.Id == orderId)).CustomerOrderNumber);
        Assert.Equal("PED-5900", (await finalDb.OpReportRecords.SingleAsync(r => r.Id == originalRecord.Id)).Number);
    }
}
