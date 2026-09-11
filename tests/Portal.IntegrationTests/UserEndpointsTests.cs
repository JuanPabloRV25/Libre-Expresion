using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Portal.Domain.Areas;
using Portal.Domain.Permissions;
using Portal.Infrastructure.Identity;
using Portal.Infrastructure.Persistence;
using Portal.Infrastructure.Persistence.Seeding;

namespace Portal.IntegrationTests;

public sealed class UserEndpointsTests
{
    private const string SuperadminPassword = "Superadmin!42";
    private const string LimitedPassword = "Limited!42";
    private const string DefinitivePassword = "Definitive!42";

    [Fact]
    public async Task Endpoints_require_authentication_permissions_and_antiforgery()
    {
        await using var factory = new PortalApiFactory();
        using var anonymousClient = factory.CreateClient();
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymousClient.GetAsync("/api/users")).StatusCode);

        var superadmin = await SeedSuperadminAsync(factory);
        var limitedDocument = await CreateLimitedUserAsync(factory);
        using var limitedClient = factory.CreateClient();
        Assert.Equal(
            HttpStatusCode.OK,
            (await LoginAsync(limitedClient, limitedDocument, LimitedPassword)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await limitedClient.GetAsync("/api/users")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await SendWithCsrfAsync(
                limitedClient,
                HttpMethod.Post,
                "/api/users",
                NewUserRequest("7000000011", "forbidden@example.test", null, []))).StatusCode);

        using var adminClient = factory.CreateClient();
        Assert.Equal(
            HttpStatusCode.OK,
            (await LoginAsync(adminClient, superadmin.Document, SuperadminPassword)).StatusCode);
        var withoutToken = await adminClient.PostAsJsonAsync(
            "/api/users",
            NewUserRequest("7000000012", "csrf@example.test", null, []));
        Assert.Equal(HttpStatusCode.BadRequest, withoutToken.StatusCode);
    }

    [Fact]
    public async Task Creator_without_role_delegation_cannot_inject_a_privileged_role()
    {
        await using var factory = new PortalApiFactory();
        await SeedSuperadminAsync(factory);
        var actor = await CreatePermissionedUserAsync(
            factory,
            [PermissionCodes.UsersCreate]);

        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var superadminRoleId = await context.Roles
            .Where(role => role.IsSystem)
            .Select(role => role.Id)
            .SingleAsync();

        using var client = factory.CreateClient();
        Assert.Equal(
            HttpStatusCode.OK,
            (await LoginAsync(client, actor.Document, LimitedPassword)).StatusCode);
        var response = await SendWithCsrfAsync(
            client,
            HttpMethod.Post,
            "/api/users",
            NewUserRequest(
                $"7{Random.Shared.NextInt64(100000000, 999999999)}",
                $"injection-{Guid.NewGuid():N}@example.test",
                null,
                [superadminRoleId]));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "role_assignment_forbidden",
            (await response.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("code")
                .GetString());
    }

    [Fact]
    public async Task Role_assigner_cannot_self_assign_a_permission_superset()
    {
        await using var factory = new PortalApiFactory();
        await SeedSuperadminAsync(factory);
        var actor = await CreatePermissionedUserAsync(
            factory,
            [PermissionCodes.UsersAssignRoles]);
        var elevatedRoleId = await CreateRoleWithPermissionsAsync(
            factory,
            [PermissionCodes.UsersView]);

        using var client = factory.CreateClient();
        Assert.Equal(
            HttpStatusCode.OK,
            (await LoginAsync(client, actor.Document, LimitedPassword)).StatusCode);
        var response = await SendWithCsrfAsync(
            client,
            HttpMethod.Put,
            $"/api/users/{actor.Id}/roles",
            new { roleIds = new[] { actor.RoleId, elevatedRoleId } });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "role_assignment_forbidden",
            (await response.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("code")
                .GetString());
    }

    [Fact]
    public async Task Superadmin_can_create_query_edit_and_assign_roles_with_validation_and_audit()
    {
        await using var factory = new PortalApiFactory();
        var superadmin = await SeedSuperadminAsync(factory);
        var fixtures = await CreateUserFixturesAsync(factory);
        using var client = factory.CreateClient();
        Assert.Equal(
            HttpStatusCode.OK,
            (await LoginAsync(client, superadmin.Document, SuperadminPassword)).StatusCode);

        var document = $"7{Random.Shared.NextInt64(100000000, 999999999)}";
        var email = $"user-{Guid.NewGuid():N}@example.test";
        var createdResponse = await SendWithCsrfAsync(
            client,
            HttpMethod.Post,
            "/api/users",
            NewUserRequest(document, email, fixtures.AreaId, [fixtures.FirstRoleId]));
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var createdBodyText = await createdResponse.Content.ReadAsStringAsync();
        var createdBody = JsonDocument.Parse(createdBodyText).RootElement;
        var created = createdBody.GetProperty("user");
        Assert.False(created.TryGetProperty("password", out _));
        Assert.False(created.TryGetProperty("passwordHash", out _));
        var userId = created.GetProperty("id").GetGuid();
        var createdAt = created.GetProperty("createdAt").GetDateTimeOffset();
        Assert.Equal("sent", createdBody.GetProperty("notificationStatus").GetString());
        Assert.Single(factory.EmailSender.Messages);
        Assert.Equal(email, factory.EmailSender.Messages.Single().To);
        Assert.True(created.GetProperty("mustChangePassword").GetBoolean());
        Assert.Equal(fixtures.AreaId, created.GetProperty("area").GetProperty("id").GetGuid());

        var duplicateDocument = await SendWithCsrfAsync(
            client,
            HttpMethod.Post,
            "/api/users",
            NewUserRequest(document.ToLowerInvariant(), $"other-{Guid.NewGuid():N}@example.test", null, []));
        Assert.Equal(HttpStatusCode.Conflict, duplicateDocument.StatusCode);
        Assert.Equal(
            "document_conflict",
            (await duplicateDocument.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        var duplicateEmail = await SendWithCsrfAsync(
            client,
            HttpMethod.Post,
            "/api/users",
            NewUserRequest($"6{Random.Shared.NextInt64(100000000, 999999999)}", email.ToUpperInvariant(), null, []));
        Assert.Equal(HttpStatusCode.Conflict, duplicateEmail.StatusCode);

        var unknownArea = await SendWithCsrfAsync(
            client,
            HttpMethod.Post,
            "/api/users",
            NewUserRequest($"6{Random.Shared.NextInt64(100000000, 999999999)}", $"area-{Guid.NewGuid():N}@example.test", Guid.NewGuid(), []));
        Assert.Equal(HttpStatusCode.BadRequest, unknownArea.StatusCode);
        Assert.Equal(
            "area_not_found",
            (await unknownArea.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        var unknownRole = await SendWithCsrfAsync(
            client,
            HttpMethod.Post,
            "/api/users",
            NewUserRequest($"6{Random.Shared.NextInt64(100000000, 999999999)}", $"role-{Guid.NewGuid():N}@example.test", null, [Guid.NewGuid()]));
        Assert.Equal(HttpStatusCode.BadRequest, unknownRole.StatusCode);
        Assert.Equal(
            "invalid_role_ids",
            (await unknownRole.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Single(factory.EmailSender.Messages);

        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/users/{userId}");
        Assert.Equal(document, detail.GetProperty("documentNumber").GetString());
        Assert.False(detail.TryGetProperty("passwordHash", out _));
        Assert.False(detail.TryGetProperty("securityStamp", out _));

        var updatedResponse = await SendWithCsrfAsync(
            client,
            HttpMethod.Put,
            $"/api/users/{userId}",
            new
            {
                firstName = "Ana María",
                lastName = "Pruebas",
                email = $"updated-{Guid.NewGuid():N}@example.test",
                areaId = fixtures.AreaId,
                advisorCode = "ASE-42",
            });
        Assert.Equal(HttpStatusCode.OK, updatedResponse.StatusCode);
        var updated = await updatedResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(document, updated.GetProperty("documentNumber").GetString());
        Assert.Equal(createdAt, updated.GetProperty("createdAt").GetDateTimeOffset());

        var rolesResponse = await SendWithCsrfAsync(
            client,
            HttpMethod.Put,
            $"/api/users/{userId}/roles",
            new { roleIds = new[] { fixtures.SecondRoleId } });
        Assert.Equal(HttpStatusCode.OK, rolesResponse.StatusCode);
        var roleIds = (await rolesResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("roles")
            .EnumerateArray()
            .Select(role => role.GetProperty("id").GetGuid())
            .ToArray();
        Assert.Equal([fixtures.SecondRoleId], roleIds);

        var filtered = await client.GetFromJsonAsync<JsonElement>(
            $"/api/users?search=ana&status=active&areaId={fixtures.AreaId}&roleId={fixtures.SecondRoleId}");
        Assert.Contains(
            filtered.EnumerateArray(),
            item => item.GetProperty("id").GetGuid() == userId);

        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var assignment = await context.Set<ApplicationUserRole>()
            .SingleAsync(item => item.UserId == userId);
        Assert.Equal(fixtures.SecondRoleId, assignment.RoleId);
        Assert.Equal(superadmin.Id, assignment.AssignedByUserId);
        var auditEvents = await context.AuditEvents
            .Where(audit => audit.EntityType == "User" && audit.EntityId == userId.ToString())
            .OrderBy(audit => audit.OccurredAt)
            .ToArrayAsync();
        Assert.Equal(
            ["user.created", "notification.user_created.sent", "user.updated", "user.roles_updated"],
            auditEvents.Select(audit => audit.Action));
        Assert.All(auditEvents, audit => Assert.Equal(superadmin.Id, audit.ActorUserId));
        Assert.DoesNotContain(
            auditEvents,
            audit => audit.Metadata?.Contains("password", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public async Task Status_and_password_reset_invalidate_existing_sessions()
    {
        await using var factory = new PortalApiFactory();
        var superadmin = await SeedSuperadminAsync(factory);
        using var adminClient = factory.CreateClient();
        Assert.Equal(
            HttpStatusCode.OK,
            (await LoginAsync(adminClient, superadmin.Document, SuperadminPassword)).StatusCode);

        var document = $"7{Random.Shared.NextInt64(100000000, 999999999)}";
        var createdResponse = await SendWithCsrfAsync(
            adminClient,
            HttpMethod.Post,
            "/api/users",
            NewUserRequest(document, $"session-{Guid.NewGuid():N}@example.test", null, []));
        var userId = (await createdResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("user")
            .GetProperty("id")
            .GetGuid();

        using var userClient = factory.CreateClient(
            new WebApplicationFactoryClientOptions { HandleCookies = true });
        Assert.Equal(
            HttpStatusCode.OK,
            (await LoginAsync(userClient, document, document)).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await SendWithCsrfAsync(
                userClient,
                HttpMethod.Post,
                "/api/auth/change-required-password",
                new { newPassword = DefinitivePassword, confirmPassword = DefinitivePassword })).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await LoginAsync(userClient, document, DefinitivePassword)).StatusCode);

        Assert.Equal(
            HttpStatusCode.OK,
            (await SendWithCsrfAsync(
                adminClient,
                HttpMethod.Patch,
                $"/api/users/{userId}/status",
                new { isActive = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await userClient.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await LoginAsync(userClient, document, DefinitivePassword)).StatusCode);

        Assert.Equal(
            HttpStatusCode.OK,
            (await SendWithCsrfAsync(
                adminClient,
                HttpMethod.Patch,
                $"/api/users/{userId}/status",
                new { isActive = true })).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await LoginAsync(userClient, document, DefinitivePassword)).StatusCode);

        var resetResponse = await SendWithCsrfAsync(
            adminClient,
            HttpMethod.Post,
            $"/api/users/{userId}/reset-password",
            new { });
        Assert.Equal(HttpStatusCode.OK, resetResponse.StatusCode);
        var reset = await resetResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(reset.GetProperty("passwordReset").GetBoolean());
        Assert.Equal("sent", reset.GetProperty("notificationStatus").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await userClient.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await LoginAsync(userClient, document, DefinitivePassword)).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await LoginAsync(userClient, document, document)).StatusCode);
        var profile = await userClient.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.True(profile.GetProperty("mustChangePassword").GetBoolean());

        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var actions = await context.AuditEvents
            .Where(audit => audit.EntityType == "User" && audit.EntityId == userId.ToString())
            .Select(audit => audit.Action)
            .ToArrayAsync();
        Assert.Contains("user.deactivated", actions);
        Assert.Contains("user.activated", actions);
        Assert.Contains("user.password_reset", actions);
        Assert.Contains("notification.password_reset.sent", actions);
    }

    [Fact]
    public async Task Notification_failure_does_not_rollback_user_creation_or_password_reset()
    {
        await using var factory = new PortalApiFactory();
        var superadmin = await SeedSuperadminAsync(factory);
        using var client = factory.CreateClient();
        Assert.Equal(
            HttpStatusCode.OK,
            (await LoginAsync(client, superadmin.Document, SuperadminPassword)).StatusCode);
        factory.EmailSender.ShouldFail = true;

        var document = $"7{Random.Shared.NextInt64(100000000, 999999999)}";
        var createdResponse = await SendWithCsrfAsync(
            client,
            HttpMethod.Post,
            "/api/users",
            NewUserRequest(document, $"failed-{Guid.NewGuid():N}@example.test", null, []));
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var createdBody = await createdResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("failed", createdBody.GetProperty("notificationStatus").GetString());
        var userId = createdBody.GetProperty("user").GetProperty("id").GetGuid();

        var resetResponse = await SendWithCsrfAsync(
            client,
            HttpMethod.Post,
            $"/api/users/{userId}/reset-password",
            new { });
        Assert.Equal(HttpStatusCode.OK, resetResponse.StatusCode);
        Assert.Equal(
            "failed",
            (await resetResponse.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("notificationStatus")
                .GetString());

        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var persisted = await context.Users.SingleAsync(user => user.Id == userId);
        Assert.True(persisted.MustChangePassword);
        var actions = await context.AuditEvents
            .Where(audit => audit.EntityId == userId.ToString())
            .Select(audit => audit.Action)
            .ToArrayAsync();
        Assert.Contains("notification.user_created.failed", actions);
        Assert.Contains("notification.password_reset.failed", actions);
    }

    [Fact]
    public async Task Last_active_superadmin_cannot_be_inactivated_or_lose_the_system_role()
    {
        await using var factory = new PortalApiFactory();
        var superadmin = await SeedSuperadminAsync(factory);
        using var client = factory.CreateClient();
        Assert.Equal(
            HttpStatusCode.OK,
            (await LoginAsync(client, superadmin.Document, SuperadminPassword)).StatusCode);

        var deactivate = await SendWithCsrfAsync(
            client,
            HttpMethod.Patch,
            $"/api/users/{superadmin.Id}/status",
            new { isActive = false });
        Assert.Equal(HttpStatusCode.Conflict, deactivate.StatusCode);
        Assert.Equal(
            "last_superadmin_required",
            (await deactivate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        var removeRole = await SendWithCsrfAsync(
            client,
            HttpMethod.Put,
            $"/api/users/{superadmin.Id}/roles",
            new { roleIds = Array.Empty<Guid>() });
        Assert.Equal(HttpStatusCode.Conflict, removeRole.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.True((await context.Users.SingleAsync(user => user.Id == superadmin.Id)).IsActive);
        Assert.Single(await context.Set<ApplicationUserRole>()
            .Where(assignment => assignment.UserId == superadmin.Id)
            .ToArrayAsync());
    }

    private static object NewUserRequest(
        string document,
        string email,
        Guid? areaId,
        Guid[] roleIds) => new
        {
            documentNumber = document,
            firstName = "Usuario",
            lastName = "Pruebas",
            email,
            areaId,
            advisorCode = "ASE-01",
            roleIds,
            isActive = true,
        };

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
            "User",
            "Administrator",
            $"users-{Guid.NewGuid():N}@example.test"));
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

    private static async Task<string> CreateLimitedUserAsync(PortalApiFactory factory)
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
        return document;
    }

    private static async Task<(Guid AreaId, Guid FirstRoleId, Guid SecondRoleId)>
        CreateUserFixturesAsync(PortalApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        var now = DateTimeOffset.UtcNow;
        var area = new Area
        {
            Id = Guid.NewGuid(),
            Name = $"Área {Guid.NewGuid():N}",
            Description = "Fixture",
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        context.Areas.Add(area);
        await context.SaveChangesAsync();

        var firstRole = NewRole("Primero", now);
        var secondRole = NewRole("Segundo", now);
        Assert.True((await roleManager.CreateAsync(firstRole)).Succeeded);
        Assert.True((await roleManager.CreateAsync(secondRole)).Succeeded);
        return (area.Id, firstRole.Id, secondRole.Id);
    }

    private static ApplicationRole NewRole(string prefix, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        Name = $"{prefix}-{Guid.NewGuid():N}",
        Description = "Fixture",
        IsActive = true,
        CreatedAt = now,
        UpdatedAt = now,
    };

    private static async Task<(Guid Id, Guid RoleId, string Document)>
        CreatePermissionedUserAsync(
            PortalApiFactory factory,
            IReadOnlyCollection<string> permissionCodes)
    {
        var roleId = await CreateRoleWithPermissionsAsync(factory, permissionCodes);
        var document = $"8{Random.Shared.NextInt64(100000000, 999999999)}";
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var now = DateTimeOffset.UtcNow;
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = document,
            Email = $"delegator-{Guid.NewGuid():N}@example.test",
            FirstName = "Delegator",
            LastName = "Limited",
            IsActive = true,
            MustChangePassword = false,
            CreatedAt = now,
            UpdatedAt = now,
        };
        Assert.True((await userManager.CreateAsync(user, LimitedPassword)).Succeeded);
        context.Set<ApplicationUserRole>().Add(new ApplicationUserRole
        {
            UserId = user.Id,
            RoleId = roleId,
            AssignedAt = now,
        });
        await context.SaveChangesAsync();
        return (user.Id, roleId, document);
    }

    private static async Task<Guid> CreateRoleWithPermissionsAsync(
        PortalApiFactory factory,
        IReadOnlyCollection<string> permissionCodes)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        var now = DateTimeOffset.UtcNow;
        var role = NewRole("Delegación", now);
        Assert.True((await roleManager.CreateAsync(role)).Succeeded);
        var permissions = await context.Permissions
            .Where(permission => permissionCodes.Contains(permission.Code))
            .ToArrayAsync();
        foreach (var permission in permissions)
        {
            context.RolePermissions.Add(new RolePermission
            {
                RoleId = role.Id,
                PermissionId = permission.Id,
                AssignedAt = now,
            });
        }

        await context.SaveChangesAsync();
        return role.Id;
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
