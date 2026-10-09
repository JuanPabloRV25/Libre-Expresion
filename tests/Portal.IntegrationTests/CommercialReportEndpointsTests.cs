using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NPOI.XSSF.UserModel;
using Portal.Application.Commercial.Reports;
using Portal.Domain.Commercial.Reports;
using Portal.Infrastructure.Commercial.Reports;
using Portal.Infrastructure.Persistence;
using Portal.Infrastructure.Persistence.Seeding;

namespace Portal.IntegrationTests;

public sealed partial class CommercialProductionOrderEndpointsTests
{
    private static byte[] ManagerFile(string lastDetail = "Etiqueta tercera", decimal amount = 823500)
    {
        using var book = new XSSFWorkbook(); var sheet = book.CreateSheet("Hoja1");
        var header = sheet.CreateRow(0);
        for (var c = 0; c < ReportExcel.SalesHeaders.Length; c++) header.CreateCell(c).SetCellValue(ReportExcel.SalesHeaders[c]);
        for (var i = 1; i <= 3; i++)
        {
            var row = sheet.CreateRow(i);
            string[] values = ["8877", "2026-05-04", "Cliente de prueba", "CONTADO", amount.ToString(System.Globalization.CultureInfo.InvariantCulture), "0", "0",
                i == 3 ? lastDetail : "Etiqueta " + i, "EMPAQUE", "Vendedor de prueba", "25280", ""];
            for (var c = 0; c < values.Length; c++) row.CreateCell(c).SetCellValue(values[c]);
        }
        sheet.CreateRow(4).CreateCell(0).SetCellValue(new string('x', 200));
        using var stream = new MemoryStream(); book.Write(stream, true); return stream.ToArray();
    }
    private static async Task<HttpResponseMessage> ReportFile(HttpClient client, string path, byte[] bytes, int? version = null, bool confirm = false)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        using var form = new MultipartFormDataContent(); form.Add(new ByteArrayContent(bytes), "file", "Manager.xlsx");
        if (version.HasValue) { form.Add(new StringContent(version.Value.ToString()), "version"); form.Add(new StringContent(confirm ? "true" : "false"), "confirm"); }
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = form };
        request.Headers.Add("X-XSRF-TOKEN", csrf.GetProperty("token").GetString());
        return await client.SendAsync(request);
    }
    private static SaveReportRequest Decisions(ReportDto report, Guid op, bool complete = false) => new(report.Version, report.Name,
        report.Data.Details.Select(d => new DetailDecision(d.Id, op, false, true, false, "Confirmada", null)).ToList(),
        complete ? report.Groups.Select(g => new SalesGroupEdit { Key = g.Key, Factura = "Escrita por auxiliar", Amount = 823500 }).ToList() : [],
        report.Data.Documents.Select(d => new DocumentDecision(d.Id, 823500, "Confirmado por auxiliar")).ToList());

    [Fact]
    public async Task Reports_preserve_details_validate_money_keep_overrides_and_protect_source_replacement()
    {
        await using var factory = new PortalApiFactory(); var admin = await SeedSuperadminAsync(factory);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await LoginAsync(client, admin.Document);
        var op = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Add(new ProductionOrderReportRecord { Id = op, Number = "25280", Client = "Cliente de prueba", Product = "Etiqueta",
                DataJson = JsonSerializer.Serialize(new OpRecordData()), CapturedByUserId = Guid.NewGuid(), CapturedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
        var bytes = ManagerFile();
        // This fixture predates the preparation engine. Legacy saves and source
        // replacement must continue using their original business contract.
        var legacyId = await SeedLegacyReportAsync(factory, admin.Id, bytes, op);
        var report = (await client.GetFromJsonAsync<ReportDto>($"/api/commercial/reports/{legacyId}"))!;
        Assert.Equal(3, report.Data.Details.Count); Assert.False(report.CanExport); Assert.All(report.Data.Details, d => Assert.True(d.Reviewed));
        Assert.Equal(823500, Assert.Single(report.Data.Documents).SourceAmount);
        Assert.Equal(report.Id, (await client.GetFromJsonAsync<ReportDto>($"/api/commercial/reports/{legacyId}"))!.Id);
        var path = $"/api/commercial/reports/{report.Id}";
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await SendWithCsrfAsync(client, HttpMethod.Post, path + "/export", new { version = report.Version })).StatusCode);
        var savedResponse = await SendWithCsrfAsync(client, HttpMethod.Put, path, Decisions(report, op));
        Assert.Equal(HttpStatusCode.OK, savedResponse.StatusCode); report = (await savedResponse.Content.ReadFromJsonAsync<ReportDto>())!;
        Assert.Equal(3, Assert.Single(report.Groups).Details.Length); Assert.Equal(823500, report.Groups[0].Amount);
        var oldVersion = report.Version;
        var complete = await SendWithCsrfAsync(client, HttpMethod.Put, path, Decisions(report, op, true));
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode); report = (await complete.Content.ReadFromJsonAsync<ReportDto>())!;
        Assert.False(report.CanExport); Assert.True(report.CanExportDraft); Assert.False(report.CanExportFinal);
        Assert.Equal(0, Assert.Single(report.Controls).Difference);
        Assert.Equal(HttpStatusCode.Conflict, (await SendWithCsrfAsync(client, HttpMethod.Put, path, Decisions(report, op, true) with { Version = oldVersion })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await SendWithCsrfAsync(client, HttpMethod.Post, path + "/export", new { version = report.Version })).StatusCode);
        var export = await SendWithCsrfAsync(client, HttpMethod.Post, path + "/export/draft", new { version = report.Version });
        Assert.True(export.IsSuccessStatusCode, await export.Content.ReadAsStringAsync());
        using (var output = new XSSFWorkbook(new MemoryStream(await export.Content.ReadAsByteArrayAsync())))
        {
            var sheet = output.GetSheet("VENTAS "); Assert.NotNull(sheet);
            Assert.Equal("Escrita por auxiliar", sheet.GetRow(3).GetCell(2).StringCellValue);
            Assert.Equal(823500, sheet.GetRow(3).GetCell(7).NumericCellValue);
            Assert.Equal(3, sheet.GetRow(3).GetCell(8).StringCellValue.Split('\n').Length);
            Assert.Equal(823500, sheet.GetRow(1).GetCell(7).NumericCellValue);
            Assert.NotNull(output.GetSheet("CUMPLIMIENTO"));
            Assert.DoesNotContain("IVA", sheet.GetRow(2).Cells.Select(c => c.ToString()));
        }
        var changed = ManagerFile("Etiqueta modificada");
        var previewResponse = await ReportFile(client, path + "/source", changed, report.Version);
        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        var preview = (await previewResponse.Content.ReadFromJsonAsync<SourceReplacementPreview>())!;
        Assert.Equal((2, 1, 1), (preview.Retained, preview.Added, preview.Removed));
        Assert.Single(preview.Preview.Data.UnappliedEdits); Assert.False(preview.Preview.CanExport);
        var unchanged = (await client.GetFromJsonAsync<ReportDto>(path))!;
        Assert.Equal(report.Data.Sha256, unchanged.Data.Sha256); Assert.False(unchanged.CanExport); Assert.True(unchanged.CanExportDraft);
        var replaced = await ReportFile(client, path + "/source", changed, report.Version, true);
        Assert.Equal(HttpStatusCode.OK, replaced.StatusCode);
        var updated = (await replaced.Content.ReadFromJsonAsync<SourceReplacementPreview>())!.Preview;
        Assert.Single(updated.Data.UnappliedEdits); Assert.False(updated.CanExport);
        var source = await client.GetByteArrayAsync($"/api/commercial/reports/sources/{report.Data.CurrentSourceId}");
        Assert.Equal(bytes, source);
    }

    [Fact]
    public async Task Reports_require_permissions_csrf_and_ownership()
    {
        await using var factory = new PortalApiFactory(); await SeedSuperadminAsync(factory);
        var first = await CreateUserInExistingRoleAsync(factory, DatabaseSeeder.CommercialAssistantRoleName, "Primera");
        var second = await CreateUserInExistingRoleAsync(factory, DatabaseSeeder.CommercialAssistantRoleName, "Segunda");
        var agent = await CreateUserInExistingRoleAsync(factory, DatabaseSeeder.CommercialAgentRoleName, "Agente");
        using var a = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true }); await LoginAsync(a, first);
        using var b = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true }); await LoginAsync(b, second);
        using var c = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true }); await LoginAsync(c, agent);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/commercial/reports")).StatusCode);
        var created = await ReportFile(a, "/api/commercial/reports", ManagerFile());
        Assert.Equal(HttpStatusCode.OK, created.StatusCode); var report = (await created.Content.ReadFromJsonAsync<ReportDto>())!;
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/api/commercial/reports/{report.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/api/commercial/reports/sources/{report.Data.CurrentSourceId}")).StatusCode);
        Assert.Empty((await b.GetFromJsonAsync<ReportSummary[]>("/api/commercial/reports"))!);
        Assert.Equal(HttpStatusCode.BadRequest, (await a.PostAsJsonAsync($"/api/commercial/reports/{report.Id}/export", new { version = report.Version })).StatusCode);
        var optionalEdit = new { version = report.Version, name = report.Name, rowEdits = Array.Empty<object>() };
        Assert.Equal(HttpStatusCode.NotFound, (await SendWithCsrfAsync(b, HttpMethod.Post, $"/api/commercial/reports/{report.Id}/prepared/preview", optionalEdit)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendWithCsrfAsync(b, HttpMethod.Put, $"/api/commercial/reports/{report.Id}/prepared", optionalEdit)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await a.PostAsJsonAsync($"/api/commercial/reports/{report.Id}/prepared/preview", optionalEdit)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await a.PutAsJsonAsync($"/api/commercial/reports/{report.Id}/prepared", optionalEdit)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendWithCsrfAsync(c, HttpMethod.Post, $"/api/commercial/reports/{report.Id}/prepared/preview", optionalEdit)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendWithCsrfAsync(c, HttpMethod.Post, "/api/commercial/reports/ops", new { data = PreparedOpData("999") })).StatusCode);
    }
}
