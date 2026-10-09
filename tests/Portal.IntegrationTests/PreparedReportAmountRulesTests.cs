using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using Portal.Application.Commercial.Reports;
using Portal.Infrastructure.Persistence;

namespace Portal.IntegrationTests;

public sealed partial class CommercialProductionOrderEndpointsTests
{
    private static async Task<ReportDto> SaveAmountDeltaAsync(HttpClient client, ReportDto report,
        object delta, params object[] decisions)
    {
        var response = await SendWithCsrfAsync(client, HttpMethod.Put, $"/api/commercial/reports/{report.Id}/prepared",
            new { version = report.Version, name = report.Name, rowEdits = new[] { delta }, decisions });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ReportDto>())!;
    }

    [Fact]
    public async Task Confirming_multiple_OPs_previews_a_blank_manual_amount_without_changing_saved_source_or_report()
    {
        await using var factory = new PortalApiFactory(); var admin = await SeedSuperadminAsync(factory);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await LoginAsync(client, admin.Document);
        var ids = await SeedPreparedOpsAsync(factory, PreparedOpData("7000", "Producto A"), PreparedOpData("7001", "Producto A"));
        var bytes = PreparedManagerFile(new("7000", "Producto A / primero"), new("7000", "Producto A / segundo"));
        var report = await CreatePreparedReportAsync(client, bytes);
        var path = $"/api/commercial/reports/{report.Id}";
        var key = Assert.Single(report.Groups).Key;
        var savedBefore = JsonSerializer.Serialize(report, ReportTestJson);
        var previewResponse = await SendWithCsrfAsync(client, HttpMethod.Post, path + "/prepared/preview", new
        {
            version = report.Version, name = report.Name, rowEdits = Array.Empty<object>(),
            decisions = new[] { OpDecision(report, key, "select_candidates", ids) }
        });
        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        var preview = (await previewResponse.Content.ReadFromJsonAsync<ReportDto>())!;
        var previewRow = Assert.Single(preview.Groups);
        Assert.Equal("7000/7001", previewRow.Op);
        Assert.Equal(2, previewRow.OpCount);
        Assert.Equal("manual", previewRow.AmountMode);
        Assert.Null(previewRow.Amount);
        Assert.False(preview.CanApprove);
        Assert.Contains(preview.Data.Preparation!.Review!.Cases.SelectMany(c => c.Findings),
            f => f.Field == "VALOR_BRUT" && f.Resolution == "pending");
        Assert.Equal(savedBefore, JsonSerializer.Serialize((await client.GetFromJsonAsync<ReportDto>(path))!, ReportTestJson));
        Assert.Equal(bytes, await client.GetByteArrayAsync($"/api/commercial/reports/sources/{report.Data.CurrentSourceId}"));

        report = await SavePreparedDecisionsAsync(client, report, OpDecision(report, key, "select_candidates", ids));
        Assert.Null(Assert.Single(report.Groups).Amount);
        Assert.False(report.CanApprove);
        var draft = await SendWithCsrfAsync(client, HttpMethod.Post, path + "/export/draft", new { version = report.Version });
        Assert.Equal(HttpStatusCode.OK, draft.StatusCode);
        using var workbook = new XSSFWorkbook(new MemoryStream(await draft.Content.ReadAsByteArrayAsync()));
        Assert.True(EmptyExcelCell(workbook.GetSheet("VENTAS ").GetRow(3).GetCell(7)));
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await SendWithCsrfAsync(client, HttpMethod.Post, path + "/approve", new { version = report.Version })).StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(321.50)]
    public async Task A_manual_multiple_OP_amount_and_free_invoice_survive_reopen_unrelated_edits_and_numeric_download(decimal total)
    {
        await using var factory = new PortalApiFactory(); var admin = await SeedSuperadminAsync(factory);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await LoginAsync(client, admin.Document);
        var ids = await SeedPreparedOpsAsync(factory, PreparedOpData("7010", "Producto A"), PreparedOpData("7011", "Producto A"));
        var bytes = PreparedManagerFile(new("7010", "Producto A / primero"), new("7010", "Producto A / segundo"));
        var report = await CreatePreparedReportAsync(client, bytes);
        var key = Assert.Single(report.Groups).Key;
        var path = $"/api/commercial/reports/{report.Id}";
        report = await SavePreparedDecisionsAsync(client, report, OpDecision(report, key, "select_candidates", ids));
        Assert.Null(report.Groups[0].Amount);
        report = await SaveAmountDeltaAsync(client, report, new { key, amount = total, setAmount = true, factura = "Texto libre" });
        Assert.Equal(total, Assert.Single(report.Groups).Amount);
        Assert.Equal("manual", report.Groups[0].AmountMode);
        Assert.True(report.CanApprove);
        report = await SaveAmountDeltaAsync(client, report, new { key, op = report.Groups[0].Op, client = "Cliente corregido" });
        report = await SaveAmountDeltaAsync(client, report, new { key, factura = "" });
        report = (await client.GetFromJsonAsync<ReportDto>(path))!;
        Assert.Equal(total, Assert.Single(report.Groups).Amount);
        Assert.Equal("", report.Groups[0].Factura);
        Assert.Equal("Cliente corregido", report.Groups[0].Client);
        Assert.Equal(2, report.Groups[0].OpCount);
        Assert.Equal(100m, Assert.Single(report.Data.Preparation!.AutomaticRows).Amount);
        Assert.Equal("F01", report.Data.Preparation.AutomaticRows[0].Factura);
        Assert.All(report.Data.Details, source => Assert.Equal(100m, source.RawAmount));
        report = await ApprovePreparedAsync(client, report);
        var exported = await SendWithCsrfAsync(client, HttpMethod.Post, path + "/export", new { version = report.Version });
        Assert.Equal(HttpStatusCode.OK, exported.StatusCode);
        using var workbook = new XSSFWorkbook(new MemoryStream(await exported.Content.ReadAsByteArrayAsync()));
        var row = workbook.GetSheet("VENTAS ").GetRow(3);
        Assert.Equal(CellType.Numeric, row.GetCell(7).CellType);
        Assert.Equal((double)total, row.GetCell(7).NumericCellValue);
        Assert.True(EmptyExcelCell(row.GetCell(2)));
        Assert.Equal(bytes, await client.GetByteArrayAsync($"/api/commercial/reports/sources/{report.Data.CurrentSourceId}"));
    }

    [Fact]
    public async Task An_atomic_OP_decision_and_manual_amount_patch_validates_against_the_resulting_multiple_OP_row()
    {
        await using var factory = new PortalApiFactory(); var admin = await SeedSuperadminAsync(factory);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await LoginAsync(client, admin.Document);
        var ids = await SeedPreparedOpsAsync(factory, PreparedOpData("7020", "Producto A"), PreparedOpData("7021", "Producto A"));
        var report = await CreatePreparedReportAsync(client, PreparedManagerFile(
            new("7020", "Producto A / primero", Amount: 100), new("7020", "Producto A / segundo", Amount: 25)));
        var key = Assert.Single(report.Groups).Key;
        report = await SaveAmountDeltaAsync(client, report, new { key, amount = 77m, setAmount = true, factura = "F99" },
            OpDecision(report, key, "select_candidates", ids));
        var row = Assert.Single(report.Groups);
        Assert.Equal("7020/7021", row.Op);
        Assert.Equal(2, row.OpCount);
        Assert.Equal("manual", row.AmountMode);
        Assert.Equal(77m, row.Amount); // The auxiliary value is neither source amount nor their sum.
        Assert.Equal("F99", row.Factura);
        Assert.Equal(new[] { 100m, 25m }, report.Data.Details.Select(d => d.RawAmount));
        Assert.True(report.CanApprove);
        Assert.Equal(0, report.Data.Preparation!.Review!.Summary.PendingCases);
    }

    [Theory]
    [InlineData(77)]
    [InlineData(null)]
    public async Task Single_OP_automatic_amount_override_rejects_the_entire_request_without_saving_invoice_or_audit(int? input)
    {
        decimal? amount = input;
        await using var factory = new PortalApiFactory(); var admin = await SeedSuperadminAsync(factory);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await LoginAsync(client, admin.Document);
        var report = await CreatePreparedReportAsync(client, PreparedManagerFile(new PreparedSourceRow("7030", "OP única", Amount: 150)));
        var path = $"/api/commercial/reports/{report.Id}";
        var row = Assert.Single(report.Groups);
        Assert.Equal(1, row.OpCount); Assert.Equal("automatic", row.AmountMode); Assert.Equal(150m, row.Amount);
        var before = JsonSerializer.Serialize(report, ReportTestJson);
        var result = await SendWithCsrfAsync(client, HttpMethod.Put, path + "/prepared", new
        {
            version = report.Version, name = "No guardar este nombre",
            rowEdits = new[] { new { key = row.Key, factura = "No guardar", amount, setAmount = true } }
        });
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal(before, JsonSerializer.Serialize((await client.GetFromJsonAsync<ReportDto>(path))!, ReportTestJson));
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await db.AuditEvents.AnyAsync(e => e.EntityId == report.Id.ToString() && e.Action == "commercial.report.prepared_edited"));
    }

    [Fact]
    public async Task A_single_resolved_OP_with_different_source_values_stays_pending_and_cannot_be_filled_manually()
    {
        await using var factory = new PortalApiFactory(); var admin = await SeedSuperadminAsync(factory);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await LoginAsync(client, admin.Document);
        var ids = await SeedPreparedOpsAsync(factory, PreparedOpData("7040", "Producto A"));
        var report = await CreatePreparedReportAsync(client, PreparedManagerFile(
            new("7040", "Producto A / primero", Amount: 100), new("7040", "Producto A / segundo", Amount: 25)));
        var key = Assert.Single(report.Groups).Key;
        report = await SavePreparedDecisionsAsync(client, report, OpDecision(report, key, "select_candidates", ids));
        var row = Assert.Single(report.Groups);
        Assert.Equal(1, row.OpCount); Assert.Equal("validation", row.AmountMode); Assert.Null(row.Amount);
        Assert.False(report.CanApprove);
        Assert.Contains(report.Data.Preparation!.Review!.Cases.SelectMany(c => c.Findings),
            f => f.Field == "VALOR_BRUT" && f.Resolution == "pending");
        var before = JsonSerializer.Serialize(report, ReportTestJson);
        var result = await SendWithCsrfAsync(client, HttpMethod.Put, $"/api/commercial/reports/{report.Id}/prepared", new
        { version = report.Version, rowEdits = new[] { new { key, amount = 125m, setAmount = true } } });
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal(before, JsonSerializer.Serialize((await client.GetFromJsonAsync<ReportDto>($"/api/commercial/reports/{report.Id}"))!, ReportTestJson));
    }

    [Fact]
    public async Task Different_history_records_for_one_normalized_OP_number_keep_a_single_automatic_source_amount()
    {
        await using var factory = new PortalApiFactory(); var admin = await SeedSuperadminAsync(factory);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await LoginAsync(client, admin.Document);
        var ids = await SeedPreparedOpsAsync(factory, PreparedOpData("7050", "Producto A"),
            PreparedOpData(" 7050 ", "Producto A", historicalClient: "Otra fila histórica"));
        var report = await CreatePreparedReportAsync(client, PreparedManagerFile(
            new("7050", "Producto A / primero", Amount: 150), new("7050", "Producto A / segundo", Amount: 150)));
        report = await SavePreparedDecisionsAsync(client, report, OpDecision(report, report.Groups[0].Key, "select_candidates", ids));
        var row = Assert.Single(report.Groups);
        Assert.Equal(1, row.OpCount); Assert.Equal("automatic", row.AmountMode); Assert.Equal(150m, row.Amount);
        Assert.Equal(2, row.DetailIds.Length);
        Assert.Equal(2, Assert.Single(report.Data.Preparation!.Review!.Decisions).HistoryIds.Length);
        Assert.True(report.CanApprove);
    }

    [Fact]
    public async Task Returning_to_one_OP_recovers_Manager_and_invalidates_the_approved_manual_revision()
    {
        await using var factory = new PortalApiFactory(); var admin = await SeedSuperadminAsync(factory);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await LoginAsync(client, admin.Document);
        var ids = await SeedPreparedOpsAsync(factory, PreparedOpData("7060", "Producto A"), PreparedOpData("7061", "Producto A"));
        var report = await CreatePreparedReportAsync(client, PreparedManagerFile(
            new("7060", "Producto A / primero", Amount: 100), new("7060", "Producto A / segundo", Amount: 100)));
        var key = Assert.Single(report.Groups).Key;
        var path = $"/api/commercial/reports/{report.Id}";
        report = await SaveAmountDeltaAsync(client, report, new { key, amount = 700m, setAmount = true },
            OpDecision(report, key, "select_candidates", ids));
        report = await ApprovePreparedAsync(client, report);
        Assert.True(report.CanExportFinal);
        var approvedVersion = report.Version;
        report = await SaveAmountDeltaAsync(client, report, new { key, op = "7060", factura = "Otro texto" });
        Assert.Equal(1, report.Groups[0].OpCount);
        Assert.Equal("automatic", report.Groups[0].AmountMode);
        Assert.Equal(100m, report.Groups[0].Amount);
        Assert.DoesNotContain(report.Groups[0].Issues, issue => issue.Contains("sin determinar"));
        Assert.Equal("Otro texto", report.Groups[0].Factura);
        Assert.False(report.CanExportFinal);
        Assert.False(Assert.Single(report.Data.Preparation!.Review!.Approvals).Valid);
        Assert.Equal(HttpStatusCode.Conflict,
            (await SendWithCsrfAsync(client, HttpMethod.Post, path + "/export", new { version = approvedVersion })).StatusCode);
        report = (await client.GetFromJsonAsync<ReportDto>(path))!;
        Assert.Equal(100m, report.Groups[0].Amount);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.True(await db.AuditEvents.AnyAsync(e => e.EntityId == report.Id.ToString() && e.Action == "commercial.report.approval_invalidated"));
    }

    [Fact]
    public async Task Confirming_one_OP_after_clearing_an_NA_amount_restores_Manager_and_leaves_the_report_ready_for_approval()
    {
        await using var factory = new PortalApiFactory(); var admin = await SeedSuperadminAsync(factory);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await LoginAsync(client, admin.Document);
        var ids = await SeedPreparedOpsAsync(factory, PreparedOpData("7070", "Producto A"));
        var bytes = PreparedManagerFile(new PreparedSourceRow("", "Producto A / único", Amount: 100));
        var report = await CreatePreparedReportAsync(client, bytes);
        var key = Assert.Single(report.Groups).Key;
        Assert.Equal("N/A", report.Groups[0].Op);
        Assert.Equal("legacy", report.Groups[0].AmountMode);
        report = await SaveAmountDeltaAsync(client, report, new { key, amount = (decimal?)null, setAmount = true });
        Assert.Null(report.Groups[0].Amount);
        Assert.Contains(report.Data.Preparation!.Review!.Cases.SelectMany(c => c.Findings),
            f => f.Code == "amount_undetermined" && f.Resolution == "pending");

        report = await SavePreparedDecisionsAsync(client, report, OpDecision(report, key, "select_candidates", ids));
        var row = Assert.Single(report.Groups);
        Assert.Equal("7070", row.Op);
        Assert.Equal(1, row.OpCount);
        Assert.Equal("automatic", row.AmountMode);
        Assert.Equal(100m, row.Amount);
        Assert.DoesNotContain(row.Issues, issue => issue.Contains("sin determinar"));
        var amountFinding = Assert.Single(report.Data.Preparation!.Review!.Cases.SelectMany(c => c.Findings),
            f => f.Code == "amount_undetermined");
        Assert.Equal("resolved", amountFinding.Resolution);
        Assert.Equal("automatic", amountFinding.Provenance);
        Assert.Equal(0, report.Data.Preparation.Review.Summary.PendingCases);
        Assert.True(report.CanApprove);
        Assert.False(ReportPreparationEngine.IncompleteTotals(report.Data.Preparation));
        Assert.Equal(100m, Assert.Single(report.Data.Details).RawAmount);
        Assert.Equal(bytes, await client.GetByteArrayAsync($"/api/commercial/reports/sources/{report.Data.CurrentSourceId}"));
        var reopened = (await client.GetFromJsonAsync<ReportDto>($"/api/commercial/reports/{report.Id}"))!;
        Assert.Equal(100m, Assert.Single(reopened.Groups).Amount);
        Assert.True(reopened.CanApprove);
        Assert.DoesNotContain(reopened.Groups[0].Issues, issue => issue.Contains("sin determinar"));
    }
}
