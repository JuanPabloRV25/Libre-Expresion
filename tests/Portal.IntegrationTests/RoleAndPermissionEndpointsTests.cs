using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Portal.Domain.Permissions;
using Portal.Domain.Commercial.ProductionOrders;
using Portal.Infrastructure.Identity;
using Portal.Infrastructure.Persistence;
using Portal.Infrastructure.Persistence.Seeding;

namespace Portal.IntegrationTests;

public sealed class RoleAndPermissionEndpointsTests
{
    private const string SuperadminPassword = "Superadmin!42";
    private const string LimitedPassword = "Limited!42";

    [Fact]
    public async Task Endpoints_require_authentication_and_permissions()
    {
        await using var factory = new PortalApiFactory();
        using var anonymousClient = factory.CreateClient();
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymousClient.GetAsync("/api/roles")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymousClient.GetAsync("/api/permissions")).StatusCode);

        await SeedSuperadminAsync(factory);
        var limitedDocument = await CreateUserAsync(factory, roleName: null);
        using var limitedClient = factory.CreateClient(
            new WebApplicationFactoryClientOptions { HandleCookies = true });
        Assert.Equal(
            HttpStatusCode.OK,
            (await LoginAsync(limitedClient, limitedDocument, LimitedPassword)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await limitedClient.GetAsync("/api/roles")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await limitedClient.GetAsync("/api/permissions")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await SendWithCsrfAsync(
                limitedClient,
                HttpMethod.Post,
                "/api/roles",
                new { name = "Sin autorización", description = "Restringido" })).StatusCode);
    }

    [Fact]
    public async Task Superadmin_can_manage_roles_and_the_official_permission_catalog()
    {
        await using var factory = new PortalApiFactory();
        var superadmin = await SeedSuperadminAsync(factory);
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions { HandleCookies = true });
        Assert.Equal(
            HttpStatusCode.OK,
            (await LoginAsync(client, superadmin.Document, SuperadminPassword)).StatusCode);

        var catalog = await client.GetFromJsonAsync<JsonElement>("/api/permissions");
        var catalogCodes = catalog.EnumerateArray()
            .Select(item => item.GetProperty("code").GetString())
            .ToArray();
        Assert.Equal(PermissionCodes.All.Count + CommercialPermissionCodes.All.Count, catalogCodes.Length);
        Assert.Equal(
            PermissionCodes.All.Concat(CommercialPermissionCodes.All).Order(StringComparer.Ordinal),
            catalogCodes.Order(StringComparer.Ordinal));

        var createdResponse = await SendWithCsrfAsync(
            client,
            HttpMethod.Post,
            "/api/roles",
            new { name = "  Editor de contenidos  ", description = "  Rol parametrizable  " });
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var created = await createdResponse.Content.ReadFromJsonAsync<JsonElement>();
        var roleId = created.GetProperty("id").GetGuid();
        var createdAt = created.GetProperty("createdAt").GetDateTimeOffset();
        Assert.False(created.GetProperty("isSystem").GetBoolean());
        Assert.True(created.GetProperty("isActive").GetBoolean());

        var duplicate = await SendWithCsrfAsync(
            client,
            HttpMethod.Post,
            "/api/roles",
            new { name = "editor DE CONTENIDOS", description = "Duplicado" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(
            "role_name_conflict",
            (await duplicate.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("code")
                .GetString());

        var updatedResponse = await SendWithCsrfAsync(
            client,
            HttpMethod.Put,
            $"/api/roles/{roleId}",
            new { name = "Editor institucional", description = "Actualizado" });
        Assert.Equal(HttpStatusCode.OK, updatedResponse.StatusCode);
        var updated = await updatedResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(createdAt, updated.GetProperty("createdAt").GetDateTimeOffset());

        var assignedResponse = await SendWithCsrfAsync(
            client,
            HttpMethod.Put,
            $"/api/roles/{roleId}/permissions",
            new { permissionCodes = new[] { PermissionCodes.RolesView, PermissionCodes.PermissionsView } });
        Assert.Equal(HttpStatusCode.OK, assignedResponse.StatusCode);
        Assert.Equal(
            [PermissionCodes.PermissionsView, PermissionCodes.RolesView],
            (await assignedResponse.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("permissionCodes")
                .EnumerateArray()
                .Select(item => item.GetString())
                .Order(StringComparer.Ordinal));

        var reducedResponse = await SendWithCsrfAsync(
            client,
            HttpMethod.Put,
            $"/api/roles/{roleId}/permissions",
            new { permissionCodes = new[] { PermissionCodes.RolesView } });
        Assert.Equal(HttpStatusCode.OK, reducedResponse.StatusCode);

        Assert.Equal(
            HttpStatusCode.OK,
            (await SendWithCsrfAsync(
                client,
                HttpMethod.Patch,
                $"/api/roles/{roleId}/status",
                new { isActive = false })).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await SendWithCsrfAsync(
                client,
                HttpMethod.Patch,
                $"/api/roles/{roleId}/status",
                new { isActive = true })).StatusCode);

        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/roles/{roleId}");
        Assert.Equal("Editor institucional", detail.GetProperty("name").GetString());
        Assert.True(detail.GetProperty("isActive").GetBoolean());
        Assert.Equal(
            PermissionCodes.RolesView,
            detail.GetProperty("permissionCodes")[0].GetString());

        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var assignment = await context.RolePermissions
            .SingleAsync(item => item.RoleId == roleId);
        Assert.Equal(superadmin.Id, assignment.AssignedByUserId);
        var auditEvents = await context.AuditEvents
            .Where(audit => audit.EntityType == "Role" && audit.EntityId == roleId.ToString())
            .OrderBy(audit => audit.OccurredAt)
            .ToArrayAsync();
        Assert.Equal(
            [
                "role.created",
                "role.updated",
                "role.permissions_updated",
                "role.permissions_updated",
                "role.deactivated",
                "role.activated",
            ],
            auditEvents.Select(audit => audit.Action));
        Assert.All(auditEvents, audit => Assert.Equal(superadmin.Id, audit.ActorUserId));
        Assert.Contains(
            auditEvents,
            audit => audit.Action == "role.permissions_updated"
                && audit.Metadata!.Contains(PermissionCodes.PermissionsView, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Superadmin_role_is_protected_and_mutations_require_antiforgery()
    {
        await using var factory = new PortalApiFactory();
        var superadmin = await SeedSuperadminAsync(factory);
        using var client = factory.CreateClient();
        Assert.Equal(
            HttpStatusCode.OK,
            (await LoginAsync(client, superadmin.Document, SuperadminPassword)).StatusCode);

        var roles = await client.GetFromJsonAsync<JsonElement>("/api/roles");
        var systemRole = roles.EnumerateArray().Single(item => item.GetProperty("name").GetString() == DatabaseSeeder.SuperadminRoleName);
        var systemRoleId = systemRole.GetProperty("id").GetGuid();

        var rename = await SendWithCsrfAsync(
            client,
            HttpMethod.Put,
            $"/api/roles/{systemRoleId}",
            new { name = "Otro nombre", description = "No permitido" });
        Assert.Equal(HttpStatusCode.Conflict, rename.StatusCode);

        var deactivate = await SendWithCsrfAsync(
            client,
            HttpMethod.Patch,
            $"/api/roles/{systemRoleId}/status",
            new { isActive = false });
        Assert.Equal(HttpStatusCode.Conflict, deactivate.StatusCode);

        var removePermission = await SendWithCsrfAsync(
            client,
            HttpMethod.Put,
            $"/api/roles/{systemRoleId}/permissions",
            new { permissionCodes = PermissionCodes.All.Concat(CommercialPermissionCodes.All).SkipLast(1).ToArray() });
        Assert.Equal(HttpStatusCode.Conflict, removePermission.StatusCode);

        var duplicatePermissions = await SendWithCsrfAsync(
            client,
            HttpMethod.Put,
            $"/api/roles/{systemRoleId}/permissions",
            new { permissionCodes = new[] { PermissionCodes.RolesView, PermissionCodes.RolesView } });
        Assert.Equal(HttpStatusCode.BadRequest, duplicatePermissions.StatusCode);

        var withoutToken = await client.PostAsJsonAsync(
            "/api/roles",
            new { name = "Sin token", description = "Inválido" });
        Assert.Equal(HttpStatusCode.BadRequest, withoutToken.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(
            PermissionCodes.All.Count + CommercialPermissionCodes.All.Count,
            await context.RolePermissions.CountAsync(
                assignment => assignment.RoleId == systemRoleId));
    }

    [Fact]
    public async Task Inactive_role_stops_granting_permissions_immediately()
    {
        await using var factory = new PortalApiFactory();
        var superadmin = await SeedSuperadminAsync(factory);
        using var adminClient = factory.CreateClient();
        Assert.Equal(
            HttpStatusCode.OK,
            (await LoginAsync(adminClient, superadmin.Document, SuperadminPassword)).StatusCode);

        var createdResponse = await SendWithCsrfAsync(
            adminClient,
            HttpMethod.Post,
            "/api/roles",
            new { name = "Consulta de roles", description = "Permiso limitado" });
        var roleId = (await createdResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
        Assert.Equal(
            HttpStatusCode.OK,
            (await SendWithCsrfAsync(
                adminClient,
                HttpMethod.Put,
                $"/api/roles/{roleId}/permissions",
                new { permissionCodes = new[] { PermissionCodes.RolesView } })).StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var role = await context.Roles.SingleAsync(candidate => candidate.Id == roleId);
            var limitedDocument = await CreateUserAsync(factory, role.Name);
            using var limitedClient = factory.CreateClient();
            Assert.Equal(
                HttpStatusCode.OK,
                (await LoginAsync(limitedClient, limitedDocument, LimitedPassword)).StatusCode);
            Assert.Equal(
                HttpStatusCode.OK,
                (await limitedClient.GetAsync("/api/roles")).StatusCode);

            Assert.Equal(
                HttpStatusCode.OK,
                (await SendWithCsrfAsync(
                    adminClient,
                    HttpMethod.Patch,
                    $"/api/roles/{roleId}/status",
                    new { isActive = false })).StatusCode);
            Assert.Equal(
                HttpStatusCode.Forbidden,
                (await limitedClient.GetAsync("/api/roles")).StatusCode);

            Assert.Equal(
                HttpStatusCode.OK,
                (await SendWithCsrfAsync(
                    adminClient,
                    HttpMethod.Patch,
                    $"/api/roles/{roleId}/status",
                    new { isActive = true })).StatusCode);
            Assert.Equal(
                HttpStatusCode.OK,
                (await limitedClient.GetAsync("/api/roles")).StatusCode);
        }
    }

    [Fact]
    public async Task Permission_delegator_cannot_add_a_permission_they_do_not_have()
    {
        await using var factory = new PortalApiFactory();
        await SeedSuperadminAsync(factory);
        var actor = await CreatePermissionDelegatorAsync(factory);
        using var client = factory.CreateClient();
        Assert.Equal(
            HttpStatusCode.OK,
            (await LoginAsync(client, actor.Document, LimitedPassword)).StatusCode);

        var response = await SendWithCsrfAsync(
            client,
            HttpMethod.Put,
            $"/api/roles/{actor.RoleId}/permissions",
            new
            {
                permissionCodes = new[]
                {
                    PermissionCodes.RolesAssignPermissions,
                    PermissionCodes.UsersView,
                },
            });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "permission_delegation_forbidden",
            (await response.Content.ReadFromJsonAsync<JsonElement>())
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
            "Role",
            "Administrator",
            $"roles-{Guid.NewGuid():N}@example.test"));
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

    private static async Task<string> CreateUserAsync(
        PortalApiFactory factory,
        string? roleName)
    {
        var document = $"8{Random.Shared.NextInt64(100000000, 999999999)}";
        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var now = DateTimeOffset.UtcNow;
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
        if (roleName is not null)
        {
            Assert.True((await userManager.AddToRoleAsync(user, roleName)).Succeeded);
        }

        return document;
    }

    private static async Task<(Guid RoleId, string Document)>
        CreatePermissionDelegatorAsync(PortalApiFactory factory)
    {
        var document = $"8{Random.Shared.NextInt64(100000000, 999999999)}";
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var now = DateTimeOffset.UtcNow;
        var role = new ApplicationRole
        {
            Id = Guid.NewGuid(),
            Name = $"Delegador-{Guid.NewGuid():N}",
            Description = "Delegación limitada",
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        Assert.True((await roleManager.CreateAsync(role)).Succeeded);
        var permission = await context.Permissions.SingleAsync(candidate =>
            candidate.Code == PermissionCodes.RolesAssignPermissions);
        context.RolePermissions.Add(new RolePermission
        {
            RoleId = role.Id,
            PermissionId = permission.Id,
            AssignedAt = now,
        });
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = document,
            Email = $"permission-delegator-{Guid.NewGuid():N}@example.test",
            FirstName = "Permission",
            LastName = "Delegator",
            IsActive = true,
            MustChangePassword = false,
            CreatedAt = now,
            UpdatedAt = now,
        };
        Assert.True((await userManager.CreateAsync(user, LimitedPassword)).Succeeded);
        context.Set<ApplicationUserRole>().Add(new ApplicationUserRole
        {
            UserId = user.Id,
            RoleId = role.Id,
            AssignedAt = now,
        });
        await context.SaveChangesAsync();
        return (role.Id, document);
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
