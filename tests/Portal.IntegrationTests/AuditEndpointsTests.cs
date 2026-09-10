using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Portal.Domain.Auditing;
using Portal.Infrastructure.Identity;
using Portal.Infrastructure.Persistence;
using Portal.Infrastructure.Persistence.Seeding;

namespace Portal.IntegrationTests;

public sealed class AuditEndpointsTests
{
    private const string SuperadminPassword = "Superadmin!42";
    private const string LimitedPassword = "Limited!42";

    [Fact]
    public async Task Endpoint_requires_authentication_and_audit_permission()
    {
        await using var factory = new PortalApiFactory();
        using var anonymousClient = factory.CreateClient();
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymousClient.GetAsync("/api/audit")).StatusCode);

        await SeedSuperadminAsync(factory);
        var limitedDocument = await CreateLimitedUserAsync(factory);
        using var limitedClient = factory.CreateClient(
            new WebApplicationFactoryClientOptions { HandleCookies = true });
        Assert.Equal(
            HttpStatusCode.OK,
            (await LoginAsync(
                limitedClient,
                limitedDocument,
                LimitedPassword)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await limitedClient.GetAsync("/api/audit")).StatusCode);
    }

    [Fact]
    public async Task Superadmin_can_page_filter_and_read_audit_events_without_creating_more()
    {
        await using var factory = new PortalApiFactory();
        var superadmin = await SeedSuperadminAsync(factory);
        var fixture = await SeedAuditEventsAsync(factory, superadmin.Id);
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions { HandleCookies = true });
        Assert.Equal(
            HttpStatusCode.OK,
            (await LoginAsync(
                client,
                superadmin.Document,
                SuperadminPassword)).StatusCode);

        var firstPage = await client.GetFromJsonAsync<JsonElement>(
            "/api/audit?page=1&pageSize=2");
        Assert.Equal(1, firstPage.GetProperty("page").GetInt32());
        Assert.Equal(2, firstPage.GetProperty("pageSize").GetInt32());
        Assert.Equal(5, firstPage.GetProperty("totalItems").GetInt32());
        Assert.Equal(3, firstPage.GetProperty("totalPages").GetInt32());
        Assert.Equal(
            [fixture.OrderedIds[0], fixture.OrderedIds[1]],
            firstPage.GetProperty("items")
                .EnumerateArray()
                .Select(item => item.GetProperty("id").GetGuid()));

        var secondPage = await client.GetFromJsonAsync<JsonElement>(
            "/api/audit?page=2&pageSize=2");
        Assert.Equal(
            [fixture.OrderedIds[2], fixture.OrderedIds[3]],
            secondPage.GetProperty("items")
                .EnumerateArray()
                .Select(item => item.GetProperty("id").GetGuid()));

        var oversizedPage = await client.GetFromJsonAsync<JsonElement>(
            "/api/audit?pageSize=500");
        Assert.Equal(100, oversizedPage.GetProperty("pageSize").GetInt32());

        await AssertSingleFilteredEventAsync(
            client,
            "/api/audit?action=AREA.CREATED",
            fixture.AreaCreatedId);
        await AssertSingleFilteredEventAsync(
            client,
            "/api/audit?entityType=role",
            fixture.RoleCreatedId);
        await AssertSingleFilteredEventAsync(
            client,
            "/api/audit?result=failed",
            fixture.RoleCreatedId);
        await AssertSingleFilteredEventAsync(
            client,
            "/api/audit?search=record-area-created",
            fixture.AreaCreatedId);

        var dateQuery = $"/api/audit?dateFrom={Uri.EscapeDataString(fixture.DateFrom.ToString("O"))}"
            + $"&dateTo={Uri.EscapeDataString(fixture.DateTo.ToString("O"))}";
        var datePage = await client.GetFromJsonAsync<JsonElement>(dateQuery);
        Assert.Equal(3, datePage.GetProperty("totalItems").GetInt32());

        var actorPage = await client.GetFromJsonAsync<JsonElement>(
            $"/api/audit?actorUserId={superadmin.Id}");
        Assert.Equal(4, actorPage.GetProperty("totalItems").GetInt32());
        Assert.All(
            actorPage.GetProperty("items").EnumerateArray(),
            item =>
            {
                Assert.Equal(
                    superadmin.Id,
                    item.GetProperty("actor").GetProperty("id").GetGuid());
                Assert.Equal(
                    "Audit Administrator",
                    item.GetProperty("actor").GetProperty("name").GetString());
            });

        var allEvents = await client.GetFromJsonAsync<JsonElement>(
            "/api/audit?pageSize=25");
        var systemEvent = allEvents.GetProperty("items")
            .EnumerateArray()
            .Single(item => item.GetProperty("id").GetGuid() == fixture.SystemEventId);
        Assert.Equal(JsonValueKind.Null, systemEvent.GetProperty("actor").ValueKind);

        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(5, await context.AuditEvents.CountAsync());
    }

    [Fact]
    public async Task Response_exposes_only_the_controlled_audit_projection()
    {
        await using var factory = new PortalApiFactory();
        var superadmin = await SeedSuperadminAsync(factory);
        var fixture = await SeedAuditEventsAsync(factory, superadmin.Id);
        using var client = factory.CreateClient();
        Assert.Equal(
            HttpStatusCode.OK,
            (await LoginAsync(
                client,
                superadmin.Document,
                SuperadminPassword)).StatusCode);

        var response = await client.GetAsync(
            $"/api/audit?action=area.created&actorUserId={superadmin.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var responseText = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("PasswordHash", responseText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret-hash", responseText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SecurityStamp", responseText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret-stamp", responseText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("antiforgery", responseText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret-token", responseText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret-cookie", responseText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(superadmin.Document, responseText, StringComparison.Ordinal);

        var page = JsonDocument.Parse(responseText).RootElement;
        var item = page.GetProperty("items")[0];
        Assert.Equal(fixture.AreaCreatedId, item.GetProperty("id").GetGuid());
        Assert.Equal(
            "Área visible",
            item.GetProperty("metadata").GetProperty("name").GetString());
        Assert.Equal(
            "Portal-E2E/1.0",
            item.GetProperty("userAgent").GetString());
    }

    [Fact]
    public async Task Endpoint_is_read_only_and_mandatory_password_change_still_blocks_it()
    {
        await using var factory = new PortalApiFactory();
        var pendingDocument = await SeedPendingSuperadminAsync(factory);
        using var pendingClient = factory.CreateClient();
        Assert.Equal(
            HttpStatusCode.OK,
            (await LoginAsync(
                pendingClient,
                pendingDocument,
                pendingDocument)).StatusCode);
        var blocked = await pendingClient.GetAsync("/api/audit");
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        Assert.Equal(
            "password_change_required",
            (await blocked.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("code")
                .GetString());

        await using var authorizedFactory = new PortalApiFactory();
        var superadmin = await SeedSuperadminAsync(authorizedFactory);
        using var client = authorizedFactory.CreateClient();
        Assert.Equal(
            HttpStatusCode.OK,
            (await LoginAsync(
                client,
                superadmin.Document,
                SuperadminPassword)).StatusCode);

        foreach (var method in new[]
        {
            HttpMethod.Post,
            HttpMethod.Put,
            HttpMethod.Patch,
            HttpMethod.Delete,
        })
        {
            using var request = new HttpRequestMessage(method, "/api/audit");
            Assert.Equal(
                HttpStatusCode.MethodNotAllowed,
                (await client.SendAsync(request)).StatusCode);
        }
    }

    private static async Task AssertSingleFilteredEventAsync(
        HttpClient client,
        string path,
        Guid expectedId)
    {
        var page = await client.GetFromJsonAsync<JsonElement>(path);
        Assert.Equal(1, page.GetProperty("totalItems").GetInt32());
        Assert.Equal(
            expectedId,
            page.GetProperty("items")[0].GetProperty("id").GetGuid());
    }

    private static async Task<(Guid Id, string Document)> SeedSuperadminAsync(
        PortalApiFactory factory)
    {
        var document = await SeedPendingSuperadminAsync(factory);
        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByNameAsync(document)
            ?? throw new InvalidOperationException("Seeded Superadmin was not found.");
        user.PasswordHash = scope.ServiceProvider
            .GetRequiredService<IPasswordHasher<ApplicationUser>>()
            .HashPassword(user, SuperadminPassword);
        user.MustChangePassword = false;
        Assert.True((await userManager.UpdateAsync(user)).Succeeded);
        return (user.Id, document);
    }

    private static async Task<string> SeedPendingSuperadminAsync(
        PortalApiFactory factory)
    {
        var document = $"9{Random.Shared.NextInt64(100000000, 999999999)}";
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.EnsureCreatedAsync();
        var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
        await seeder.SeedAsync(new DatabaseSeedSettings(
            document,
            "Audit",
            "Administrator",
            $"audit-{Guid.NewGuid():N}@example.test"));
        return document;
    }

    private static async Task<string> CreateLimitedUserAsync(
        PortalApiFactory factory)
    {
        var document = $"8{Random.Shared.NextInt64(100000000, 999999999)}";
        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var now = DateTimeOffset.UtcNow;
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = document,
            Email = $"limited-audit-{Guid.NewGuid():N}@example.test",
            FirstName = "Limited",
            LastName = "User",
            IsActive = true,
            MustChangePassword = false,
            CreatedAt = now,
            UpdatedAt = now,
        };
        Assert.True((await userManager.CreateAsync(user, LimitedPassword)).Succeeded);
        return document;
    }

    private static async Task<AuditFixture> SeedAuditEventsAsync(
        PortalApiFactory factory,
        Guid actorId)
    {
        var origin = new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
        var areaCreatedId = Guid.NewGuid();
        var systemEventId = Guid.NewGuid();
        var roleCreatedId = Guid.NewGuid();
        var userCreatedId = Guid.NewGuid();
        var areaDeactivatedId = Guid.NewGuid();

        var auditEvents = new[]
        {
            new AuditEvent
            {
                Id = areaCreatedId,
                ActorUserId = actorId,
                Action = "area.created",
                EntityType = "Area",
                EntityId = "record-area-created",
                Result = "SUCCESS",
                OccurredAt = origin,
                CorrelationId = Guid.NewGuid(),
                UserAgent = "Portal-E2E/1.0",
                Metadata = """{"Name":"Área visible","PasswordHash":"secret-hash","SecurityStamp":"secret-stamp","Cookie":"secret-cookie","AntiforgeryToken":"secret-token"}""",
            },
            new AuditEvent
            {
                Id = systemEventId,
                ActorUserId = null,
                Action = "area.updated",
                EntityType = "Area",
                EntityId = "record-area-system",
                Result = "Success",
                OccurredAt = origin.AddMinutes(1),
                Metadata = null,
            },
            new AuditEvent
            {
                Id = roleCreatedId,
                ActorUserId = actorId,
                Action = "role.created",
                EntityType = "Role",
                EntityId = "record-role",
                Result = "FAILED",
                OccurredAt = origin.AddMinutes(2),
                Metadata = """{"Name":"Rol de prueba"}""",
            },
            new AuditEvent
            {
                Id = userCreatedId,
                ActorUserId = actorId,
                Action = "user.created",
                EntityType = "User",
                EntityId = "record-user",
                Result = "DENIED",
                OccurredAt = origin.AddMinutes(3),
                Metadata = """{"NotificationStatus":"pending_integration"}""",
            },
            new AuditEvent
            {
                Id = areaDeactivatedId,
                ActorUserId = actorId,
                Action = "area.deactivated",
                EntityType = "Area",
                EntityId = "record-area-disabled",
                Result = "Success",
                OccurredAt = origin.AddMinutes(4),
                Metadata = """{"IsActive":false}""",
            },
        };

        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        context.AuditEvents.AddRange(auditEvents);
        await context.SaveChangesAsync();

        return new AuditFixture(
            areaCreatedId,
            systemEventId,
            roleCreatedId,
            [
                areaDeactivatedId,
                userCreatedId,
                roleCreatedId,
                systemEventId,
                areaCreatedId,
            ],
            origin.AddMinutes(1),
            origin.AddMinutes(3));
    }

    private static Task<HttpResponseMessage> LoginAsync(
        HttpClient client,
        string document,
        string password) => SendWithCsrfAsync(
            client,
            HttpMethod.Post,
            "/api/auth/login",
            new { documentNumber = document, password });

    private static async Task<HttpResponseMessage> SendWithCsrfAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        object body)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        using var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add(
            "X-XSRF-TOKEN",
            csrf.GetProperty("token").GetString());
        return await client.SendAsync(request);
    }

    private sealed record AuditFixture(
        Guid AreaCreatedId,
        Guid SystemEventId,
        Guid RoleCreatedId,
        IReadOnlyList<Guid> OrderedIds,
        DateTimeOffset DateFrom,
        DateTimeOffset DateTo);
}
