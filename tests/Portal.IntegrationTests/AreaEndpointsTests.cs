using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Portal.Infrastructure.Identity;
using Portal.Infrastructure.Persistence;
using Portal.Infrastructure.Persistence.Seeding;

namespace Portal.IntegrationTests;

public sealed class AreaEndpointsTests
{
    private const string SuperadminPassword = "Superadmin!42";
    private const string LimitedPassword = "Limited!42";

    [Fact]
    public async Task Endpoints_require_authentication_and_the_corresponding_permission()
    {
        await using var factory = new PortalApiFactory();
        using var anonymousClient = factory.CreateClient();
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymousClient.GetAsync("/api/areas")).StatusCode);

        var limitedDocument = await SeedLimitedUserAsync(factory);
        using var limitedClient = factory.CreateClient(
            new WebApplicationFactoryClientOptions { HandleCookies = true });
        Assert.Equal(
            HttpStatusCode.OK,
            (await LoginAsync(limitedClient, limitedDocument, LimitedPassword)).StatusCode);

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await limitedClient.GetAsync("/api/areas")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await SendWithCsrfAsync(
                limitedClient,
                HttpMethod.Post,
                "/api/areas",
                new { name = "Restringida", description = "No autorizada" })).StatusCode);
    }

    [Fact]
    public async Task Superadmin_can_complete_the_area_lifecycle_with_audit_events()
    {
        await using var factory = new PortalApiFactory();
        var superadmin = await SeedSuperadminAsync(factory);
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions { HandleCookies = true });
        Assert.Equal(
            HttpStatusCode.OK,
            (await LoginAsync(client, superadmin.Document, SuperadminPassword)).StatusCode);

        var createdResponse = await SendWithCsrfAsync(
            client,
            HttpMethod.Post,
            "/api/areas",
            new { name = "  Comunicaciones  ", description = "  Área institucional  " });
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var created = await createdResponse.Content.ReadFromJsonAsync<JsonElement>();
        var areaId = created.GetProperty("id").GetGuid();
        var createdAt = created.GetProperty("createdAt").GetDateTimeOffset();
        Assert.Equal("Comunicaciones", created.GetProperty("name").GetString());
        Assert.True(created.GetProperty("isActive").GetBoolean());

        var duplicate = await SendWithCsrfAsync(
            client,
            HttpMethod.Post,
            "/api/areas",
            new { name = "comunicaciones", description = "Duplicada" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(
            "area_name_conflict",
            (await duplicate.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("code")
                .GetString());

        var listed = await client.GetFromJsonAsync<JsonElement>(
            "/api/areas?search=comunica&status=active");
        Assert.Single(listed.EnumerateArray());

        var updatedResponse = await SendWithCsrfAsync(
            client,
            HttpMethod.Put,
            $"/api/areas/{areaId}",
            new { name = "Comunicaciones Estratégicas", description = "Actualizada" });
        Assert.Equal(HttpStatusCode.OK, updatedResponse.StatusCode);
        var updated = await updatedResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(createdAt, updated.GetProperty("createdAt").GetDateTimeOffset());
        Assert.True(updated.GetProperty("updatedAt").GetDateTimeOffset() >= createdAt);

        var deactivatedResponse = await SendWithCsrfAsync(
            client,
            HttpMethod.Patch,
            $"/api/areas/{areaId}/status",
            new { isActive = false });
        Assert.Equal(HttpStatusCode.OK, deactivatedResponse.StatusCode);

        var inactive = await client.GetFromJsonAsync<JsonElement>(
            "/api/areas?status=inactive");
        Assert.Contains(
            inactive.EnumerateArray(),
            item => item.GetProperty("id").GetGuid() == areaId);

        var activatedResponse = await SendWithCsrfAsync(
            client,
            HttpMethod.Patch,
            $"/api/areas/{areaId}/status",
            new { isActive = true });
        Assert.Equal(HttpStatusCode.OK, activatedResponse.StatusCode);

        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/areas/{areaId}");
        Assert.Equal("Comunicaciones Estratégicas", detail.GetProperty("name").GetString());
        Assert.True(detail.GetProperty("isActive").GetBoolean());

        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var auditEvents = await context.AuditEvents
            .Where(audit => audit.EntityType == "Area" && audit.EntityId == areaId.ToString())
            .OrderBy(audit => audit.OccurredAt)
            .ToListAsync();
        Assert.Equal(
            ["area.created", "area.updated", "area.deactivated", "area.activated"],
            auditEvents.Select(audit => audit.Action));
        Assert.All(auditEvents, audit =>
        {
            Assert.Equal(superadmin.Id, audit.ActorUserId);
            Assert.Equal("Success", audit.Result);
            Assert.NotEqual(default, audit.OccurredAt);
        });
    }

    [Fact]
    public async Task Mutations_validate_input_and_require_antiforgery()
    {
        await using var factory = new PortalApiFactory();
        var superadmin = await SeedSuperadminAsync(factory);
        using var client = factory.CreateClient();
        Assert.Equal(
            HttpStatusCode.OK,
            (await LoginAsync(client, superadmin.Document, SuperadminPassword)).StatusCode);

        var invalid = await SendWithCsrfAsync(
            client,
            HttpMethod.Post,
            "/api/areas",
            new { name = " ", description = "Inválida" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(
            "validation_error",
            (await invalid.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("code")
                .GetString());

        var withoutToken = await client.PostAsJsonAsync(
            "/api/areas",
            new { name = "Sin token", description = "Inválida" });
        Assert.Equal(HttpStatusCode.BadRequest, withoutToken.StatusCode);
        Assert.Equal(
            "invalid_antiforgery_token",
            (await withoutToken.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("code")
                .GetString());
    }

    private static async Task<(Guid Id, string Document)> SeedSuperadminAsync(
        PortalApiFactory factory)
    {
        var document = $"9{Random.Shared.NextInt64(100000000, 999999999)}";
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.EnsureCreatedAsync();
        var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
        await seeder.SeedAsync(new DatabaseSeedSettings(
            document,
            "Area",
            "Administrator",
            $"areas-{Guid.NewGuid():N}@example.test"));

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByNameAsync(document)
            ?? throw new InvalidOperationException("Seeded Superadmin was not found.");
        user.PasswordHash = scope.ServiceProvider
            .GetRequiredService<IPasswordHasher<ApplicationUser>>()
            .HashPassword(user, SuperadminPassword);
        user.MustChangePassword = false;
        var update = await userManager.UpdateAsync(user);
        Assert.True(update.Succeeded);
        return (user.Id, document);
    }

    private static async Task<string> SeedLimitedUserAsync(PortalApiFactory factory)
    {
        await SeedSuperadminAsync(factory);
        var document = $"8{Random.Shared.NextInt64(100000000, 999999999)}";
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var now = DateTimeOffset.UtcNow;
        var role = new ApplicationRole
        {
            Id = Guid.NewGuid(),
            Name = $"Limited-{Guid.NewGuid():N}",
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        Assert.True((await roleManager.CreateAsync(role)).Succeeded);

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = document,
            Email = $"limited-{Guid.NewGuid():N}@example.test",
            FirstName = "Limited",
            LastName = "User",
            IsActive = true,
            MustChangePassword = false,
            CreatedAt = now,
            UpdatedAt = now,
        };
        Assert.True((await userManager.CreateAsync(user, LimitedPassword)).Succeeded);
        Assert.True((await userManager.AddToRoleAsync(user, role.Name!)).Succeeded);
        await context.SaveChangesAsync();
        return document;
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
        request.Headers.Add("X-XSRF-TOKEN", csrf.GetProperty("token").GetString());
        return await client.SendAsync(request);
    }
}
