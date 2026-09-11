using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Portal.Domain.Permissions;
using Portal.Infrastructure.Identity;
using Portal.Infrastructure.Persistence;
using Portal.Infrastructure.Persistence.Seeding;

namespace Portal.IntegrationTests;

public sealed class AuthenticationEndpointsTests
{
    private const string DefinitivePassword = "Definitive!42";

    [Fact]
    public async Task Login_RebuildsProfileWithThe17EffectivePermissions()
    {
        await using var factory = new PortalApiFactory();
        var credentials = await SeedSuperadminAsync(factory, mustChangePassword: false);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
        });

        var login = await PostWithCsrfAsync(client, "/api/auth/login", new
        {
            documentNumber = credentials.Document,
            password = credentials.Password,
        });

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var authCookie = login.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith("PortalLibreExpresion.Auth=", StringComparison.Ordinal));
        Assert.Contains("httponly", authCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", authCookie, StringComparison.OrdinalIgnoreCase);

        var profile = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");
        var permissions = profile.GetProperty("permissions")
            .EnumerateArray()
            .Select(item => item.GetString())
            .ToArray();

        Assert.Equal(credentials.Document, profile.GetProperty("documentNumber").GetString());
        Assert.Equal(PermissionCodes.All.Order(), permissions.Order());
        Assert.Equal(17, permissions.Length);
        Assert.Contains("Superadmin", profile.GetProperty("roles").EnumerateArray().Select(item => item.GetString()));
    }

    [Fact]
    public async Task MandatoryPasswordChange_RestrictsSessionAndInvalidatesItAfterSuccess()
    {
        await using var factory = new PortalApiFactory();
        var credentials = await SeedSuperadminAsync(factory, mustChangePassword: true);
        using var client = factory.CreateClient();

        var login = await LoginAsync(client, credentials.Document, credentials.Password);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var profile = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.True(profile.GetProperty("mustChangePassword").GetBoolean());
        Assert.Empty(profile.GetProperty("permissions").EnumerateArray());

        var forbidden = await PostWithCsrfAsync(client, "/api/auth/change-password", new
        {
            currentPassword = credentials.Password,
            newPassword = DefinitivePassword,
            confirmPassword = DefinitivePassword,
        });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var changed = await PostWithCsrfAsync(client, "/api/auth/change-required-password", new
        {
            newPassword = DefinitivePassword,
            confirmPassword = DefinitivePassword,
        });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal(
            "sent",
            (await changed.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("notificationStatus")
                .GetString());
        Assert.Single(factory.EmailSender.Messages);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(client, credentials.Document, credentials.Password)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(client, credentials.Document, DefinitivePassword)).StatusCode);
    }

    [Fact]
    public async Task Login_RejectsInactiveUserAndLocksRepeatedFailures()
    {
        await using (var inactiveFactory = new PortalApiFactory())
        {
            var inactive = await SeedSuperadminAsync(inactiveFactory, mustChangePassword: false, isActive: false);
            using var inactiveClient = inactiveFactory.CreateClient();
            var response = await LoginAsync(inactiveClient, inactive.Document, inactive.Password);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal("inactive_account", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }

        await using var lockedFactory = new PortalApiFactory();
        var active = await SeedSuperadminAsync(lockedFactory, mustChangePassword: false);
        using var client = lockedFactory.CreateClient();
        HttpResponseMessage? lastResponse = null;

        for (var attempt = 0; attempt < 5; attempt++)
        {
            lastResponse?.Dispose();
            lastResponse = await LoginAsync(client, active.Document, "Wrong!Password1");
        }

        Assert.NotNull(lastResponse);
        Assert.Equal((HttpStatusCode)423, lastResponse.StatusCode);
        lastResponse.Dispose();
    }

    [Fact]
    public async Task PersonalPasswordChangeKeepsSessionAndLogoutInvalidatesIt()
    {
        await using var factory = new PortalApiFactory();
        var credentials = await SeedSuperadminAsync(factory, mustChangePassword: false);
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(client, credentials.Document, credentials.Password)).StatusCode);

        var changed = await PostWithCsrfAsync(client, "/api/auth/change-password", new
        {
            currentPassword = credentials.Password,
            newPassword = DefinitivePassword,
            confirmPassword = DefinitivePassword,
        });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal(
            "sent",
            (await changed.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("notificationStatus")
                .GetString());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);

        var logout = await PostWithCsrfAsync(client, "/api/auth/logout", new { });
        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(client, credentials.Document, credentials.Password)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(client, credentials.Document, DefinitivePassword)).StatusCode);
    }

    [Fact]
    public async Task Email_failure_does_not_rollback_a_personal_password_change()
    {
        await using var factory = new PortalApiFactory();
        var credentials = await SeedSuperadminAsync(factory, mustChangePassword: false);
        using var client = factory.CreateClient();
        Assert.Equal(
            HttpStatusCode.OK,
            (await LoginAsync(client, credentials.Document, credentials.Password)).StatusCode);
        factory.EmailSender.ShouldFail = true;

        var changed = await PostWithCsrfAsync(client, "/api/auth/change-password", new
        {
            currentPassword = credentials.Password,
            newPassword = DefinitivePassword,
            confirmPassword = DefinitivePassword,
        });

        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal(
            "failed",
            (await changed.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("notificationStatus")
                .GetString());
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await LoginAsync(client, credentials.Document, credentials.Password)).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await LoginAsync(client, credentials.Document, DefinitivePassword)).StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Contains(
            await context.AuditEvents.ToArrayAsync(),
            audit => audit.Action == "notification.password_changed.failed"
                && audit.Result == "Failed");
    }

    [Fact]
    public async Task PostWithoutAntiforgeryTokenIsRejected()
    {
        await using var factory = new PortalApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            documentNumber = "unknown",
            password = "unknown",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_antiforgery_token", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    private static async Task<(string Document, string Password)> SeedSuperadminAsync(
        PortalApiFactory factory,
        bool mustChangePassword,
        bool isActive = true)
    {
        var document = $"9{Random.Shared.NextInt64(100000000, 999999999)}";
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.EnsureCreatedAsync();
        var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
        await seeder.SeedAsync(new DatabaseSeedSettings(
            document,
            "Integration",
            "Test",
            $"integration-{Guid.NewGuid():N}@example.test"));

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByNameAsync(document)
            ?? throw new InvalidOperationException("Seeded test user was not found.");
        user.MustChangePassword = mustChangePassword;
        user.IsActive = isActive;
        var updated = await userManager.UpdateAsync(user);
        Assert.True(updated.Succeeded);
        return (document, document);
    }

    private static Task<HttpResponseMessage> LoginAsync(
        HttpClient client,
        string document,
        string password) => PostWithCsrfAsync(client, "/api/auth/login", new
        {
            documentNumber = document,
            password,
        });

    private static async Task<HttpResponseMessage> PostWithCsrfAsync(
        HttpClient client,
        string path,
        object body)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("X-XSRF-TOKEN", csrf.GetProperty("token").GetString());
        return await client.SendAsync(request);
    }
}
