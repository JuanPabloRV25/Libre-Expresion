using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Portal.Application.Commercial.Reports;
using Portal.Domain.Commercial.Reports;
using Portal.Infrastructure.Persistence;
using Portal.Infrastructure.Persistence.Seeding;

namespace Portal.IntegrationTests;

public sealed partial class CommercialProductionOrderEndpointsTests
{
    private sealed class ReportsPostgresFactAttribute : FactAttribute
    {
        public ReportsPostgresFactAttribute()
        { if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PORTAL_TEST_POSTGRES_CONNECTION"))) Skip = "Requires isolated PostgreSQL with synthetic data only."; }
    }
    private sealed class ReportSaveGate : SaveChangesInterceptor
    {
        public string Target { get; set; } = "reports";
        private int arrived; private TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Reset(string target) { Target = target; arrived = 0; ready = new(TaskCreationOptions.RunContinuationsAsynchronously); }
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result, CancellationToken ct = default)
        {
            var hit = Target switch
            {
                "reports" => data.Context!.ChangeTracker.Entries<CommercialReport>().Any(e => e.State == EntityState.Added),
                "report_updates" => data.Context!.ChangeTracker.Entries<CommercialReport>().Any(e => e.State == EntityState.Modified),
                "ops" => data.Context!.ChangeTracker.Entries<ProductionOrderReportRecord>().Any(e => e.State == EntityState.Added),
                _ => false
            };
            if (hit) { if (Interlocked.Increment(ref arrived) == 2) ready.TrySetResult(); await ready.Task.WaitAsync(TimeSpan.FromSeconds(30), ct); }
            return result;
        }
    }
    [ReportsPostgresFact]
    [Trait("Category", "PostgresReports")]
    public async Task Reports_migration_idempotent_source_and_OP_capture_are_atomic_in_PostgreSQL()
    {
        var connection = Environment.GetEnvironmentVariable("PORTAL_TEST_POSTGRES_CONNECTION")!;
        if (new Npgsql.NpgsqlConnectionStringBuilder(connection).Database != "portal_plan003_tests")
            throw new InvalidOperationException("Only the dedicated portal_plan003_tests database may run this test.");
        // Preparation is serialized by the service and a PostgreSQL advisory
        // transaction lock. Do not wait for a second insert inside that lock.
        var gate = new ReportSaveGate { Target = "disabled" };
        await using var factory = new PortalApiFactory(postgresConnectionString: connection, saveInterceptor: gate, disableCommercialOutboxWorker: true);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.EnsureDeletedAsync(); await db.Database.MigrateAsync();
            Assert.False(db.Database.HasPendingModelChanges());
        }
        var admin = await SeedSuperadminAsync(factory);
        using var a = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        using var b = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await LoginAsync(a, admin.Document); await LoginAsync(b, admin.Document);
        var bytes = ManagerFile();
        var imports = await Task.WhenAll(ReportFile(a, "/api/commercial/reports", bytes), ReportFile(b, "/api/commercial/reports", bytes));
        Assert.All(imports, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var prepared = await imports[0].Content.ReadFromJsonAsync<ReportDto>();
        var reused = await imports[1].Content.ReadFromJsonAsync<ReportDto>();
        Assert.Equal(prepared!.Id, reused!.Id);
        Assert.Equal(4, prepared.Data.Preparation!.RuleVersion);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(1, await db.CommercialReports.CountAsync()); Assert.Equal(1, await db.CommercialReportSources.CountAsync());
        }
        gate.Reset("ops");
        var assistant = await CreateUserInExistingRoleAsync(factory, DatabaseSeeder.CommercialAssistantRoleName, "Auxiliar");
        var draft = await CreateReviewDraftAsync(factory, a); var orderId = draft.GetProperty("id").GetGuid();
        var review = await SendWithCsrfAsync(a, HttpMethod.Post, $"/api/commercial/production-orders/{orderId}/submit-for-review", new { version = draft.GetProperty("version").GetInt32() });
        Assert.Equal(HttpStatusCode.OK, review.StatusCode);
        var pending = await review.Content.ReadFromJsonAsync<JsonElement>(); var version = pending.GetProperty("version").GetInt32();
        await LoginAsync(a, assistant); await LoginAsync(b, assistant);
        var results = await Task.WhenAll(SendWithCsrfAsync(a, HttpMethod.Post, $"/api/commercial/production-orders/{orderId}/submit", new { version, customerOrderNumber = "25280" }),
            SendWithCsrfAsync(b, HttpMethod.Post, $"/api/commercial/production-orders/{orderId}/submit", new { version, customerOrderNumber = "25280" }));
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK); Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var record = Assert.Single(await db.OpReportRecords.Where(r => r.ProductionOrderId == orderId).ToArrayAsync());
            Assert.Equal(version + 1, record.OrderVersion);
            Assert.Equal("25280", record.Number);
            Assert.Equal(version + 1, (await db.ProductionOrders.SingleAsync(o => o.Id == orderId)).Version);
        }
    }

    [ReportsPostgresFact]
    [Trait("Category", "PostgresReports")]
    public async Task Report_decisions_and_approvals_use_real_PostgreSQL_version_concurrency()
    {
        var connection = Environment.GetEnvironmentVariable("PORTAL_TEST_POSTGRES_CONNECTION")!;
        if (new Npgsql.NpgsqlConnectionStringBuilder(connection).Database != "portal_plan003_tests")
            throw new InvalidOperationException("Only the dedicated portal_plan003_tests database may run this test.");
        var gate = new ReportSaveGate { Target = "disabled" };
        await using var factory = new PortalApiFactory(postgresConnectionString: connection, saveInterceptor: gate, disableCommercialOutboxWorker: true);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.EnsureDeletedAsync(); await db.Database.MigrateAsync();
        }
        var admin = await SeedSuperadminAsync(factory);
        using var a = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        using var b = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await LoginAsync(a, admin.Document); await LoginAsync(b, admin.Document);
        var report = await CreatePreparedReportAsync(a, PreparedManagerFile(new PreparedSourceRow("", "Producto / detalle")));
        var path = $"/api/commercial/reports/{report.Id}";
        var decision = OpDecision(report, report.Groups[0].Key, "keep_na");
        var request = new { version = report.Version, name = report.Name, rowEdits = Array.Empty<object>(), decisions = new[] { decision } };
        gate.Reset("report_updates");
        var saves = await Task.WhenAll(SendWithCsrfAsync(a, HttpMethod.Put, path + "/prepared", request),
            SendWithCsrfAsync(b, HttpMethod.Put, path + "/prepared", request));
        Assert.Single(saves, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(saves, response => response.StatusCode == HttpStatusCode.Conflict);
        report = (await a.GetFromJsonAsync<ReportDto>(path))!;
        Assert.Single(report.Data.Preparation!.Review!.Decisions);
        Assert.Equal(0, report.Data.Preparation.Review.Summary.PendingCases);
        var approvalsRequest = new { version = report.Version };
        gate.Reset("report_updates");
        var approvals = await Task.WhenAll(SendWithCsrfAsync(a, HttpMethod.Post, path + "/approve", approvalsRequest),
            SendWithCsrfAsync(b, HttpMethod.Post, path + "/approve", approvalsRequest));
        Assert.Single(approvals, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(approvals, response => response.StatusCode == HttpStatusCode.Conflict);
        gate.Reset("disabled");
        report = (await a.GetFromJsonAsync<ReportDto>(path))!;
        Assert.Single(report.Data.Preparation!.Review!.Approvals);
        Assert.True(report.CanExportFinal);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(1, await db.AuditEvents.CountAsync(e => e.EntityId == report.Id.ToString() && e.Action == "commercial.report.decided"));
            Assert.Equal(1, await db.AuditEvents.CountAsync(e => e.EntityId == report.Id.ToString() && e.Action == "commercial.report.approved"));
        }
    }

    [ReportsPostgresFact]
    [Trait("Category", "PostgresReports")]
    public async Task PostgreSQL_failed_audit_insert_rolls_back_decision_cell_version_and_review_together()
    {
        var connection = Environment.GetEnvironmentVariable("PORTAL_TEST_POSTGRES_CONNECTION")!;
        if (new Npgsql.NpgsqlConnectionStringBuilder(connection).Database != "portal_plan003_tests")
            throw new InvalidOperationException("Only the dedicated portal_plan003_tests database may run this test.");
        await using var factory = new PortalApiFactory(postgresConnectionString: connection, disableCommercialOutboxWorker: true);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.EnsureDeletedAsync(); await db.Database.MigrateAsync();
        }
        var admin = await SeedSuperadminAsync(factory);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await LoginAsync(client, admin.Document);
        var report = await CreatePreparedReportAsync(client, PreparedManagerFile(new PreparedSourceRow("", "Producto / detalle")));
        var path = $"/api/commercial/reports/{report.Id}";
        report = (await client.GetFromJsonAsync<ReportDto>(path))!;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION reject_report_decision_audit() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF NEW."Action" = 'commercial.report.decided' THEN
                        RAISE EXCEPTION 'Synthetic audit failure for report atomicity test';
                    END IF;
                    RETURN NEW;
                END; $$;
                CREATE TRIGGER reject_report_decision_audit BEFORE INSERT ON "AuditEvents"
                FOR EACH ROW EXECUTE FUNCTION reject_report_decision_audit();
                """);
        }
        var failed = await SendWithCsrfAsync(client, HttpMethod.Put, path + "/prepared", new
        {
            version = report.Version, name = "No guardar", rowEdits = new[] { new { key = report.Groups[0].Key, factura = "NO GUARDAR" } },
            decisions = new[] { OpDecision(report, report.Groups[0].Key, "set_manual_op", op: "OP sin persistir") }
        });
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        var unchanged = (await client.GetFromJsonAsync<ReportDto>(path))!;
        Assert.Equal(JsonSerializer.Serialize(report, ReportTestJson), JsonSerializer.Serialize(unchanged, ReportTestJson));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.False(await db.AuditEvents.AnyAsync(e => e.EntityId == report.Id.ToString() && e.Action == "commercial.report.decided"));
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_report_decision_audit ON \"AuditEvents\"; DROP FUNCTION reject_report_decision_audit();");
        }
    }
}
