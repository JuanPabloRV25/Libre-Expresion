using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NPOI.XSSF.UserModel;
using Portal.Application.Commercial.Reports;
using Portal.Infrastructure.Commercial.Reports;
using Portal.Infrastructure.Persistence;

namespace Portal.IntegrationTests;

public sealed partial class CommercialProductionOrderEndpointsTests
{
    private static byte[] ManagerWithOmittedSourceRow(bool invalidAcceptedDate = false)
    {
        using var book = new XSSFWorkbook(new MemoryStream(PreparedManagerFile(
            new PreparedSourceRow("4500", "Producto A / Detalle de venta", Date: invalidAcceptedDate ? "no-fecha" : "2026-10-07"))));
        var sheet = book.GetSheetAt(0);
        sheet.CreateRow(2); // A fully blank row is not an incidence.
        var row = sheet.CreateRow(3);
        row.CreateCell(0).SetCellValue("Texto ajeno a los campos de venta");
        row.CreateCell(7).SetCellValue("Texto original preservado");
        row.CreateCell(12).SetCellValue("Celda adicional fuera de las doce columnas");
        using var stream = new MemoryStream();
        book.Write(stream, true);
        return stream.ToArray();
    }

    [Fact]
    public void Preparation_source_evidence_preserves_exact_omitted_row_cells_and_existing_pending_guard()
    {
        var bytes = ManagerWithOmittedSourceRow();
        var hash = SHA256.HashData(bytes);
        var data = ReportExcel.ReadSales(bytes, Guid.NewGuid(), "Origen.xlsx", "source-hash", preparationMode: true);
        var source = Assert.Single(data.Details);
        Assert.Equal(2, source.SourceRow);
        Assert.Equal("4500", source.ManagerOp);
        Assert.Equal(100m, source.RawAmount);
        var warning = Assert.Single(data.SourceWarningEvidence);
        Assert.Equal("Se omitieron 1 filas sin datos de FECHA ni VALOR_BRUT (textos sueltos o pies del archivo).", Assert.Single(data.Warnings));
        Assert.Equal(data.Warnings[0], warning.Warning);
        var row = Assert.Single(warning.Rows);
        Assert.Equal("Origen.xlsx", row.SourceFile);
        Assert.Equal("Hoja1", row.Sheet);
        Assert.Equal(4, row.SourceRow);
        Assert.Equal(13, row.Cells.Length);
        Assert.Equal(new ReportSourceCellEvidence("A", "NUMERO", "Texto ajeno a los campos de venta"), row.Cells[0]);
        Assert.Equal(new ReportSourceCellEvidence("B", "FECHA", ""), row.Cells[1]);
        Assert.Equal(new ReportSourceCellEvidence("E", "VALOR_BRUT", ""), row.Cells[4]);
        Assert.Equal(new ReportSourceCellEvidence("H", "DETALLE", "Texto original preservado"), row.Cells[7]);
        Assert.Equal(new ReportSourceCellEvidence("M", "", "Celda adicional fuera de las doce columnas"), row.Cells[12]);

        var preparation = ReportPreparationEngine.Prepare(data, []);
        Assert.Single(preparation.Rows);
        var fileCase = Assert.Single(preparation.Review!.Cases, c => c.Scope == "file");
        var finding = Assert.Single(fileCase.Findings);
        Assert.Equal("source_warning", finding.Code);
        Assert.Equal("validation", finding.InitialClassification);
        Assert.Equal("pending", finding.Resolution);
        Assert.Empty(finding.AllowedActions);
        Assert.Empty(finding.Evidence); // Omitted text is not product/OP association evidence.
        Assert.Same(row, Assert.Single(finding.SourceRows));
        Assert.Equal(1, ReportReviewPolicy.Summary(data).FilePendingCases);
        Assert.Throws<ReportValidationException>(() => ReportReviewPolicy.Approve(preparation, "auxiliar", 1, DateTimeOffset.UnixEpoch));
        Assert.Equal(hash, SHA256.HashData(bytes));
    }

    [Fact]
    public void Recovery_source_evidence_reads_omitted_rows_without_reparsing_accepted_sales()
    {
        var bytes = ManagerWithOmittedSourceRow(invalidAcceptedDate: true);
        Assert.Throws<ReportValidationException>(() => ReportExcel.ReadSales(bytes, Guid.NewGuid(), "Origen.xlsx", "hash", preparationMode: true));
        var recovered = ReportExcel.ReadOmittedSalesRows(bytes, "Origen.xlsx");
        var row = Assert.Single(recovered.Rows);
        Assert.Equal(4, row.SourceRow);
        Assert.Equal("Texto ajeno a los campos de venta", row.Cells[0].Value);
        Assert.DoesNotContain(recovered.Rows, r => r.SourceRow == 2);
    }

    [Fact]
    public async Task Legacy_GET_source_evidence_is_recovered_without_writing_snapshot_version_audit_or_original_bytes()
    {
        await using var factory = new PortalApiFactory();
        var admin = await SeedSuperadminAsync(factory);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await LoginAsync(client, admin.Document);
        var bytes = ManagerWithOmittedSourceRow();
        var created = await CreatePreparedReportAsync(client, bytes);
        var initialFinding = Assert.Single(created.Data.Preparation!.Review!.Cases, c => c.Scope == "file").Findings.Single();
        Assert.Equal(4, Assert.Single(initialFinding.SourceRows).SourceRow);
        string savedJson;
        int auditCount;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var stored = await db.CommercialReports.SingleAsync(r => r.Id == created.Id);
            var data = JsonSerializer.Deserialize<SalesReportData>(stored.DataJson, ReportTestJson)!;
            data.SourceWarningEvidence.Clear();
            foreach (var finding in data.Preparation!.Review!.Cases.SelectMany(c => c.Findings)) finding.SourceRows = [];
            stored.DataJson = JsonSerializer.Serialize(data, ReportTestJson);
            await db.SaveChangesAsync();
            savedJson = stored.DataJson;
            auditCount = await db.AuditEvents.CountAsync();
        }

        var loaded = (await client.GetFromJsonAsync<ReportDto>($"/api/commercial/reports/{created.Id}"))!;
        var recovered = Assert.Single(loaded.Data.Preparation!.Review!.Cases, c => c.Scope == "file").Findings.Single();
        Assert.Equal(4, Assert.Single(recovered.SourceRows).SourceRow);
        Assert.Equal("pending", recovered.Resolution);
        Assert.Empty(recovered.AllowedActions);
        Assert.Equal(created.Version, loaded.Version);
        Assert.Equal(created.UpdatedAt, loaded.UpdatedAt);
        Assert.Equal(JsonSerializer.Serialize(created.Groups, ReportTestJson), JsonSerializer.Serialize(loaded.Groups, ReportTestJson));
        Assert.False(loaded.CanApprove);
        Assert.False(loaded.CanExportFinal);
        Assert.True(loaded.CanExportDraft);
        Assert.Equal(bytes, await client.GetByteArrayAsync($"/api/commercial/reports/sources/{created.Data.CurrentSourceId}"));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var stored = await db.CommercialReports.AsNoTracking().SingleAsync(r => r.Id == created.Id);
            Assert.Equal(savedJson, stored.DataJson);
            Assert.Equal(created.Version, stored.Version);
            Assert.Equal(auditCount, await db.AuditEvents.CountAsync());
        }
    }
}
