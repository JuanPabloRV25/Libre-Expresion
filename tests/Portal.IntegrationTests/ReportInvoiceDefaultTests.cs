using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NPOI.XSSF.UserModel;
using Portal.Application.Commercial.Reports;
using Portal.Infrastructure.Persistence;

namespace Portal.IntegrationTests;

public sealed partial class CommercialProductionOrderEndpointsTests
{
    [Theory]
    [InlineData(2, false, "F01")]
    [InlineData(3, false, "F01")]
    [InlineData(3, true, "")]
    public async Task Saved_invoice_default_is_the_same_in_GET_preview_and_Excel_without_rewriting_saved_data(
        int ruleVersion, bool explicitBlank, string expected)
    {
        await using var factory = new PortalApiFactory(); var admin = await SeedSuperadminAsync(factory);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await LoginAsync(client, admin.Document);
        var bytes = PreparedManagerFile(new PreparedSourceRow("6900", "Original único"));
        var report = await CreatePreparedReportAsync(client, bytes);
        var key = report.Groups[0].Key;
        string savedJson;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var saved = await db.CommercialReports.SingleAsync(row => row.Id == report.Id);
            var data = JsonSerializer.Deserialize<SalesReportData>(saved.DataJson, ReportTestJson)!;
            data.Preparation!.RuleVersion = ruleVersion;
            if (ruleVersion == 2) data.Preparation.Review = null;
            data.Preparation.Rows[0] = data.Preparation.Rows[0] with { Factura = "" };
            data.Preparation.AutomaticRows[0] = data.Preparation.AutomaticRows[0] with { Factura = "" };
            if (explicitBlank) data.Preparation.RowEdits = [new() { Key = key, Factura = "" }];
            savedJson = saved.DataJson = JsonSerializer.Serialize(data, ReportTestJson);
            await db.SaveChangesAsync();
        }
        var path = $"/api/commercial/reports/{report.Id}";
        var reopened = (await client.GetFromJsonAsync<ReportDto>(path))!;
        Assert.Equal(expected, Assert.Single(reopened.Groups).Factura);
        Assert.Equal("F01", Assert.Single(reopened.Data.Preparation!.AutomaticRows).Factura);
        Assert.Equal(ruleVersion, reopened.Data.Preparation.RuleVersion);
        Assert.Equal(report.Version, reopened.Version);
        Assert.Equal("6900", reopened.Groups[0].Op);
        Assert.Equal(100m, reopened.Groups[0].Amount);
        Assert.Equal(JsonSerializer.Serialize(report.Data.Details, ReportTestJson), JsonSerializer.Serialize(reopened.Data.Details, ReportTestJson));

        var previewResponse = await SendWithCsrfAsync(client, HttpMethod.Post, path + "/prepared/preview", new
        { version = reopened.Version, rowEdits = new[] { new { key, factura = "00039" } } });
        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        Assert.Equal("00039", Assert.Single((await previewResponse.Content.ReadFromJsonAsync<ReportDto>())!.Groups).Factura);
        var noChange = await SendWithCsrfAsync(client, HttpMethod.Put, path + "/prepared", new
        { version = reopened.Version, rowEdits = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.OK, noChange.StatusCode);
        Assert.Equal(reopened.Version, (await noChange.Content.ReadFromJsonAsync<ReportDto>())!.Version);
        var export = await SendWithCsrfAsync(client, HttpMethod.Post, path + "/export/draft", new { version = reopened.Version });
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        using (var book = new XSSFWorkbook(new MemoryStream(await export.Content.ReadAsByteArrayAsync())))
            Assert.Equal(expected, book.GetSheet("VENTAS ").GetRow(3).GetCell(2).StringCellValue);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var saved = await db.CommercialReports.AsNoTracking().SingleAsync(row => row.Id == report.Id);
            Assert.Equal(savedJson, saved.DataJson);
            Assert.Equal(report.Version, saved.Version);
            Assert.Equal(bytes, (await db.CommercialReportSources.AsNoTracking().SingleAsync()).OriginalBytes);
            Assert.Equal(0, await db.AuditEvents.CountAsync(a => a.EntityId == report.Id.ToString() && a.Action == "commercial.report.prepared_edited"));
        }
    }

    [Fact]
    public async Task An_old_blank_invoice_approval_does_not_approve_F01_and_new_approval_exports_the_effective_invoice()
    {
        await using var factory = new PortalApiFactory(); var admin = await SeedSuperadminAsync(factory);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await LoginAsync(client, admin.Document);
        var report = await CreatePreparedReportAsync(client, PreparedManagerFile(new PreparedSourceRow("6910", "Original único")));
        string originalJson, originalApproval;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var saved = await db.CommercialReports.SingleAsync(row => row.Id == report.Id);
            var data = JsonSerializer.Deserialize<SalesReportData>(saved.DataJson, ReportTestJson)!;
            data.Preparation!.RuleVersion = 3;
            data.Preparation.Rows[0] = data.Preparation.Rows[0] with { Factura = "" };
            data.Preparation.AutomaticRows[0] = data.Preparation.AutomaticRows[0] with { Factura = "" };
            ReportReviewPolicy.Approve(data.Preparation, "aprobación anterior", report.Version, DateTimeOffset.UtcNow);
            originalApproval = JsonSerializer.Serialize(data.Preparation.Review!.Approvals[0], ReportTestJson);
            originalJson = saved.DataJson = JsonSerializer.Serialize(data, ReportTestJson);
            await db.SaveChangesAsync();
        }
        var path = $"/api/commercial/reports/{report.Id}";
        var reopened = (await client.GetFromJsonAsync<ReportDto>(path))!;
        Assert.Equal("F01", reopened.Groups[0].Factura);
        Assert.True(reopened.CanApprove);
        Assert.False(reopened.CanExportFinal);
        Assert.Equal(originalApproval, JsonSerializer.Serialize(reopened.Data.Preparation!.Review!.Approvals[0], ReportTestJson));
        var rejected = await SendWithCsrfAsync(client, HttpMethod.Post, path + "/export", new { version = reopened.Version });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);
        await using (var scope = factory.Services.CreateAsyncScope())
            Assert.Equal(originalJson, (await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
                .CommercialReports.AsNoTracking().SingleAsync(row => row.Id == report.Id)).DataJson);
        var approved = await ApprovePreparedAsync(client, reopened);
        Assert.Equal(report.Version + 1, approved.Version);
        Assert.False(approved.Data.Preparation!.Review!.Approvals[0].Valid);
        var exported = await SendWithCsrfAsync(client, HttpMethod.Post, path + "/export", new { version = approved.Version });
        Assert.Equal(HttpStatusCode.OK, exported.StatusCode);
        using var workbook = new XSSFWorkbook(new MemoryStream(await exported.Content.ReadAsByteArrayAsync()));
        Assert.Equal("F01", workbook.GetSheet("VENTAS ").GetRow(3).GetCell(2).StringCellValue);
    }

    [Fact]
    public async Task Legacy_without_invoice_edit_downloads_F01_and_explicit_legacy_blank_is_preserved()
    {
        await using var factory = new PortalApiFactory(); var admin = await SeedSuperadminAsync(factory);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await LoginAsync(client, admin.Document);
        var id = await SeedLegacyReportAsync(factory, admin.Id, PreparedManagerFile(new PreparedSourceRow("6920", "Legacy original")));
        var path = $"/api/commercial/reports/{id}";
        var report = (await client.GetFromJsonAsync<ReportDto>(path))!;
        Assert.Null(report.Data.Preparation);
        Assert.Equal("F01", Assert.Single(report.Groups).Factura);
        var exported = await SendWithCsrfAsync(client, HttpMethod.Post, path + "/export/draft", new { version = report.Version });
        Assert.Equal(HttpStatusCode.OK, exported.StatusCode);
        using (var book = new XSSFWorkbook(new MemoryStream(await exported.Content.ReadAsByteArrayAsync())))
            Assert.Equal("F01", book.GetSheet("VENTAS ").GetRow(3).GetCell(2).StringCellValue);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var saved = await db.CommercialReports.SingleAsync(row => row.Id == id);
            var data = JsonSerializer.Deserialize<SalesReportData>(saved.DataJson, ReportTestJson)!;
            Assert.Empty(data.Edits);
            data.Edits.Add(new() { Key = report.Groups[0].Key, Factura = "" });
            saved.DataJson = JsonSerializer.Serialize(data, ReportTestJson);
            await db.SaveChangesAsync();
        }
        report = (await client.GetFromJsonAsync<ReportDto>(path))!;
        Assert.Equal("", report.Groups[0].Factura);
        exported = await SendWithCsrfAsync(client, HttpMethod.Post, path + "/export/draft", new { version = report.Version });
        Assert.Equal(HttpStatusCode.OK, exported.StatusCode);
        using var workbook = new XSSFWorkbook(new MemoryStream(await exported.Content.ReadAsByteArrayAsync()));
        Assert.Equal("", workbook.GetSheet("VENTAS ").GetRow(3).GetCell(2).StringCellValue);
    }
}
