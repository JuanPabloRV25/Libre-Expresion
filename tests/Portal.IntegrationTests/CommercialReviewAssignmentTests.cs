using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Portal.Domain.Commercial.ProductionOrders;
using Portal.Infrastructure.Commercial.ProductionOrders;
using Portal.Infrastructure.Identity;
using Portal.Infrastructure.Notifications;
using Portal.Infrastructure.Persistence;
using Portal.Infrastructure.Persistence.Seeding;

namespace Portal.IntegrationTests;

public sealed partial class CommercialProductionOrderEndpointsTests
{
    [Fact]
    public async Task Multiple_assistants_are_explicitly_assigned_and_resends_discard_superseded_mail()
    {
        using var factory = new PortalApiFactory(disableCommercialOutboxWorker: true);
        await SeedSuperadminAsync(factory);
        var agentDocument = await CreatePermissionedUserAsync(factory, "Agente", [
            CommercialPermissionCodes.OrdersView, CommercialPermissionCodes.OrdersCreate,
            CommercialPermissionCodes.OrdersSubmitForReview, CommercialPermissionCodes.OrdersEditCommercial]);
        var aDocument = await CreateUserInExistingRoleAsync(factory, DatabaseSeeder.CommercialAssistantRoleName, "Ana");
        var bDocument = await CreateUserInExistingRoleAsync(factory, DatabaseSeeder.CommercialAssistantRoleName, "Beatriz");
        using var agent = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        using var a = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        using var b = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(agent, agentDocument)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(a, aDocument)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(b, bDocument)).StatusCode);
        var created = await CreateReviewDraftAsync(factory, agent);
        var id = created.GetProperty("id").GetGuid();
        var version = created.GetProperty("version").GetInt32();
        var candidates = await agent.GetFromJsonAsync<JsonElement>($"/api/commercial/production-orders/{id}/reviewers");
        Assert.Equal(2, candidates.GetArrayLength());
        var aId = candidates[0].GetProperty("id").GetGuid();
        var bId = candidates[1].GetProperty("id").GetGuid();
        Assert.Equal("Ana Prueba", candidates[0].GetProperty("name").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await b.GetAsync($"/api/commercial/production-orders/{id}/reviewers")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await SendWithCsrfAsync(agent, HttpMethod.Post, $"/api/commercial/production-orders/{id}/submit-for-review", new { version })).StatusCode);
        await AssertNoReviewTransitionAsync(factory, id, version);
        var submitted = await SendWithCsrfAsync(agent, HttpMethod.Post, $"/api/commercial/production-orders/{id}/submit-for-review", new { version, reviewerUserId = aId });
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        var pending = await submitted.Content.ReadFromJsonAsync<JsonElement>();
        version = pending.GetProperty("version").GetInt32();
        Assert.Equal(aId, pending.GetProperty("currentAssignee").GetProperty("id").GetGuid());
        var reloaded = await agent.GetFromJsonAsync<JsonElement>($"/api/commercial/production-orders/{id}");
        Assert.Equal(aId, reloaded.GetProperty("reviewOwner").GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.Forbidden, (await SendWithCsrfAsync(b, HttpMethod.Post, $"/api/commercial/production-orders/{id}/submit", new { version, customerOrderNumber = "TEST" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendWithCsrfAsync(b, HttpMethod.Post, $"/api/commercial/production-orders/{id}/return-for-correction", new { version, reason = "No autorizada" })).StatusCode);
        var returned = await SendWithCsrfAsync(a, HttpMethod.Post, $"/api/commercial/production-orders/{id}/return-for-correction", new { version, reason = "Corregir documento" });
        Assert.Equal(HttpStatusCode.OK, returned.StatusCode);
        var correction = await returned.Content.ReadFromJsonAsync<JsonElement>();
        version = correction.GetProperty("version").GetInt32();
        Assert.Equal(aId, correction.GetProperty("reviewOwner").GetProperty("id").GetGuid());
        var resent = await SendWithCsrfAsync(agent, HttpMethod.Post, $"/api/commercial/production-orders/{id}/submit-for-review", new { version, reviewerUserId = bId });
        Assert.Equal(HttpStatusCode.OK, resent.StatusCode);
        pending = await resent.Content.ReadFromJsonAsync<JsonElement>();
        version = pending.GetProperty("version").GetInt32();
        Assert.Equal(bId, pending.GetProperty("reviewOwner").GetProperty("id").GetGuid());
        Assert.Contains(pending.GetProperty("history").EnumerateArray(), entry => entry.GetProperty("note").GetString()?.Contains(aId.ToString()) == true);
        Assert.Contains(pending.GetProperty("history").EnumerateArray(), entry => entry.GetProperty("note").GetString()?.Contains(bId.ToString()) == true);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendWithCsrfAsync(a, HttpMethod.Post, $"/api/commercial/production-orders/{id}/return-for-correction", new { version, reason = "Ya no asignada" })).StatusCode);
        var worker = CreateReviewWorker(factory);
        await worker.ProcessNextAsync(default);
        await worker.ProcessNextAsync(default);
        await worker.ProcessNextAsync(default);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var events = await db.CommercialNotificationOutbox.Where(item => item.ProductionOrderId == id).ToArrayAsync();
            Assert.Equal(3, events.Length);
            Assert.Equal(2, events.Count(item => item.Status == CommercialOutboxStatus.Failed && item.LastError!.StartsWith("Superseded:")));
            Assert.Single(events, item => item.Status == CommercialOutboxStatus.Sent && item.RecipientUserId == bId);
            var audits = await db.AuditEvents.Where(item => item.EntityId == id.ToString() && item.Action == "commercial.production_order.submitted_for_review").ToArrayAsync();
            Assert.Equal(2, audits.Length);
            Assert.Contains(audits, audit => JsonDocument.Parse(audit.Metadata!).RootElement.GetProperty("ReviewerUserId").GetGuid() == aId);
            Assert.Contains(audits, audit => JsonDocument.Parse(audit.Metadata!).RootElement.GetProperty("ReviewerUserId").GetGuid() == bId
                && JsonDocument.Parse(audit.Metadata!).RootElement.GetProperty("PreviousReviewerUserId").GetGuid() == aId);
            var bEmail = (await db.Users.SingleAsync(user => user.Id == bId)).Email;
            Assert.Single(factory.EmailSender.Messages, message => message.To == bEmail);
        }
        var second = await CreateReviewDraftAsync(factory, agent);
        var secondId = second.GetProperty("id").GetGuid();
        var secondSubmit = await SendWithCsrfAsync(agent, HttpMethod.Post, $"/api/commercial/production-orders/{secondId}/submit-for-review", new { version = second.GetProperty("version").GetInt32(), reviewerUserId = aId });
        Assert.Equal(HttpStatusCode.OK, secondSubmit.StatusCode);
        factory.EmailSender.ShouldFail = true;
        await worker.ProcessNextAsync(default);
        await using var finalScope = factory.Services.CreateAsyncScope();
        var finalDb = finalScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(aId, (await finalDb.ProductionOrders.SingleAsync(order => order.Id == secondId)).CurrentAssigneeUserId);
        var failedMail = await finalDb.CommercialNotificationOutbox.SingleAsync(item => item.ProductionOrderId == secondId);
        Assert.Equal(CommercialOutboxStatus.Pending, failedMail.Status);
        Assert.Equal(1, failedMail.Attempts);
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("email")]
    [InlineData("role")]
    [InlineData("permission")]
    [InlineData("grant")]
    [InlineData("membership")]
    [InlineData("foreign")]
    public async Task Eligibility_is_revalidated_without_assigning_an_alternative(string mutation)
    {
        using var factory = new PortalApiFactory(disableCommercialOutboxWorker: true);
        var admin = await SeedSuperadminAsync(factory);
        var document = await CreateUserInExistingRoleAsync(factory, DatabaseSeeder.CommercialAssistantRoleName, "Ana");
        await CreateUserInExistingRoleAsync(factory, DatabaseSeeder.CommercialAssistantRoleName, "Beatriz");
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(client, admin.Document)).StatusCode);
        var draft = await CreateReviewDraftAsync(factory, client);
        var id = draft.GetProperty("id").GetGuid(); var version = draft.GetProperty("version").GetInt32();
        var candidates = await client.GetFromJsonAsync<JsonElement>($"/api/commercial/production-orders/{id}/reviewers");
        Assert.Equal(2, candidates.GetArrayLength());
        Guid selected;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await db.Users.SingleAsync(user => user.UserName == document);
            selected = user.Id;
            var role = await db.Roles.SingleAsync(role => role.NormalizedName == "AUXILIAR COMERCIAL");
            var permission = await db.Permissions.SingleAsync(permission => permission.Code == CommercialPermissionCodes.OrdersReview);
            switch (mutation)
            {
                case "inactive": user.IsActive = false; break;
                case "email": user.Email = "invalid email"; break;
                case "role": role.IsActive = false; break;
                case "permission": permission.IsActive = false; break;
                case "grant": db.RolePermissions.Remove(await db.RolePermissions.SingleAsync(grant => grant.RoleId == role.Id && grant.PermissionId == permission.Id)); break;
                case "membership": db.Set<ApplicationUserRole>().Remove(await db.Set<ApplicationUserRole>().SingleAsync(member => member.RoleId == role.Id && member.UserId == user.Id)); break;
                case "foreign": selected = admin.Id; break;
            }
            await db.SaveChangesAsync();
        }
        var response = await SendWithCsrfAsync(client, HttpMethod.Post, $"/api/commercial/production-orders/{id}/submit-for-review", new { version, reviewerUserId = selected });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await AssertNoReviewTransitionAsync(factory, id, version);
    }

    [Fact]
    public async Task Candidates_are_minimal_distinct_and_include_new_accounts_without_a_session()
    {
        using var factory = new PortalApiFactory(disableCommercialOutboxWorker: true);
        var admin = await SeedSuperadminAsync(factory);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(client, admin.Document)).StatusCode);
        var draft = await CreateReviewDraftAsync(factory, client); var id = draft.GetProperty("id").GetGuid();
        Assert.Equal(0, (await client.GetFromJsonAsync<JsonElement>($"/api/commercial/production-orders/{id}/reviewers")).GetArrayLength());
        Assert.Equal(HttpStatusCode.Conflict, (await SendWithCsrfAsync(client, HttpMethod.Post, $"/api/commercial/production-orders/{id}/submit-for-review", new { version = draft.GetProperty("version").GetInt32() })).StatusCode);
        var doc = await CreateUserInExistingRoleAsync(factory, DatabaseSeeder.CommercialAssistantRoleName, "Ana");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await db.Users.SingleAsync(user => user.UserName == doc); user.MustChangePassword = true;
            var otherRole = await db.Roles.FirstAsync(role => role.NormalizedName != "AUXILIAR COMERCIAL");
            db.Set<ApplicationUserRole>().Add(new ApplicationUserRole { UserId = user.Id, RoleId = otherRole.Id, AssignedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
        var single = await client.GetFromJsonAsync<JsonElement>($"/api/commercial/production-orders/{id}/reviewers");
        Assert.Equal(1, single.GetArrayLength());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/commercial/production-orders/{id}/submit-for-review", new { version = draft.GetProperty("version").GetInt32(), reviewerUserId = single[0].GetProperty("id").GetGuid() })).StatusCode);
        var legacySubmit = await SendWithCsrfAsync(client, HttpMethod.Post, $"/api/commercial/production-orders/{id}/submit-for-review", new { version = draft.GetProperty("version").GetInt32() });
        Assert.Equal(HttpStatusCode.OK, legacySubmit.StatusCode);
        Assert.Equal(single[0].GetProperty("id").GetGuid(), (await legacySubmit.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("reviewOwner").GetProperty("id").GetGuid());
        draft = await CreateReviewDraftAsync(factory, client);
        id = draft.GetProperty("id").GetGuid();
        await CreateUserInExistingRoleAsync(factory, DatabaseSeeder.CommercialAssistantRoleName, "Ana");
        var sameNames = await client.GetFromJsonAsync<JsonElement>($"/api/commercial/production-orders/{id}/reviewers");
        Assert.Equal(2, sameNames.GetArrayLength());
        foreach (var candidate in sameNames.EnumerateArray())
        {
            Assert.Equal(3, candidate.EnumerateObject().Count());
            Assert.False(candidate.TryGetProperty("document", out _));
            Assert.NotNull(candidate.GetProperty("secondaryLabel").GetString());
        }
        Assert.NotEqual(sameNames[0].GetProperty("secondaryLabel").GetString(), sameNames[1].GetProperty("secondaryLabel").GetString());
    }

    [Fact]
    public void Notification_generations_handle_same_reviewer_legacy_payloads_and_correction_edits()
    {
        var userId = Guid.NewGuid(); var instant = DateTimeOffset.UtcNow;
        var order = new ProductionOrder { Status = ProductionOrderStatus.PendingCommercialReview, CurrentAssigneeUserId = userId,
            ReviewOwnerUserId = userId, ReviewSubmittedAt = instant, Version = 4 };
        var notification = new CommercialNotificationOutbox { RecipientUserId = userId, NotificationType = "commercial-review-requested",
            CreatedAt = instant, PayloadJson = JsonSerializer.Serialize(new { ActorUserId = Guid.NewGuid(), OrderVersion = 2 }) };
        Assert.False(CommercialNotificationOutboxWorker.IsCurrentNotification(notification, order));
        notification.PayloadJson = JsonSerializer.Serialize(new { ActorUserId = Guid.NewGuid(), OrderVersion = 4 });
        Assert.True(CommercialNotificationOutboxWorker.IsCurrentNotification(notification, order));
        notification.PayloadJson = "{}";
        Assert.True(CommercialNotificationOutboxWorker.IsCurrentNotification(notification, order));
        order.ReviewSubmittedAt = instant.AddMinutes(1);
        Assert.False(CommercialNotificationOutboxWorker.IsCurrentNotification(notification, order));
        notification.NotificationType = "commercial-correction-required";
        notification.PayloadJson = JsonSerializer.Serialize(new { ActorUserId = Guid.NewGuid(), OrderVersion = 5, ReviewReturnedAt = instant });
        order.Status = ProductionOrderStatus.CorrectionRequired; order.ReviewReturnedAt = instant; order.Version = 8;
        Assert.True(CommercialNotificationOutboxWorker.IsCurrentNotification(notification, order));
        order.ReviewReturnedAt = instant.AddMinutes(1);
        Assert.False(CommercialNotificationOutboxWorker.IsCurrentNotification(notification, order));
    }

    [PostgresFact]
    [Trait("Category", "Postgres")]
    public async Task Concurrent_review_submissions_commit_one_assignment_history_and_outbox_in_PostgreSQL()
    {
        var connection = Environment.GetEnvironmentVariable("PORTAL_TEST_POSTGRES_CONNECTION")
            ?? throw new InvalidOperationException("Run Category=Postgres with an isolated PORTAL_TEST_POSTGRES_CONNECTION.");
        var gate = new ReviewSaveGate();
        using var factory = new PortalApiFactory(postgresConnectionString: connection, saveInterceptor: gate, disableCommercialOutboxWorker: true);
        var admin = await SeedSuperadminAsync(factory);
        var aDoc = await CreateUserInExistingRoleAsync(factory, DatabaseSeeder.CommercialAssistantRoleName, "Ana");
        var bDoc = await CreateUserInExistingRoleAsync(factory, DatabaseSeeder.CommercialAssistantRoleName, "Beatriz");
        using var client1 = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        using var client2 = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(client1, admin.Document)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(client2, admin.Document)).StatusCode);
        var draft = await CreateReviewDraftAsync(factory, client1); var id = draft.GetProperty("id").GetGuid(); var version = draft.GetProperty("version").GetInt32();
        var candidates = await client1.GetFromJsonAsync<JsonElement>($"/api/commercial/production-orders/{id}/reviewers");
        var aId = candidates[0].GetProperty("id").GetGuid(); var bId = candidates[1].GetProperty("id").GetGuid();
        var results = await Task.WhenAll(
            SendWithCsrfAsync(client1, HttpMethod.Post, $"/api/commercial/production-orders/{id}/submit-for-review", new { version, reviewerUserId = aId }),
            SendWithCsrfAsync(client2, HttpMethod.Post, $"/api/commercial/production-orders/{id}/submit-for-review", new { version, reviewerUserId = bId }));
        Assert.Single(results, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(results, response => response.StatusCode == HttpStatusCode.Conflict);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var order = await db.ProductionOrders.SingleAsync(order => order.Id == id);
        Assert.Equal(version + 1, order.Version);
        Assert.Equal(order.CurrentAssigneeUserId, order.ReviewOwnerUserId);
        var item = Assert.Single(await db.CommercialNotificationOutbox.Where(item => item.ProductionOrderId == id).ToArrayAsync());
        Assert.Equal(order.ReviewOwnerUserId, item.RecipientUserId);
        Assert.Single(await db.Set<ProductionOrderStatusHistory>().Where(entry => entry.ProductionOrderId == id && entry.Status == ProductionOrderStatus.PendingCommercialReview).ToArrayAsync());
    }

    private static async Task<JsonElement> CreateReviewDraftAsync(PortalApiFactory factory, HttpClient client)
    {
        var response = await SendWithCsrfAsync(client, HttpMethod.Post, "/api/commercial/production-orders", CommercialPayload());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var draft = await response.Content.ReadFromJsonAsync<JsonElement>();
        await CompleteDocumentChecklistAsync(factory, draft.GetProperty("id").GetGuid());
        return draft;
    }

    private static async Task AssertNoReviewTransitionAsync(PortalApiFactory factory, Guid id, int version)
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var order = await db.ProductionOrders.SingleAsync(order => order.Id == id);
        Assert.Equal(ProductionOrderStatus.Draft, order.Status); Assert.Equal(version, order.Version); Assert.Null(order.ReviewOwnerUserId);
        Assert.False(await db.CommercialNotificationOutbox.AnyAsync(item => item.ProductionOrderId == id));
    }

    private static CommercialNotificationOutboxWorker CreateReviewWorker(PortalApiFactory factory) => new(
        factory.Services.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System,
        factory.Services.GetRequiredService<IOptions<EmailOptions>>(), NullLogger<CommercialNotificationOutboxWorker>.Instance);

    private sealed class PostgresFactAttribute : FactAttribute
    {
        public PostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PORTAL_TEST_POSTGRES_CONNECTION")))
                Skip = "Requires isolated PostgreSQL; set PORTAL_TEST_POSTGRES_CONNECTION and run Category=Postgres.";
        }
    }

    private sealed class ReviewSaveGate : SaveChangesInterceptor
    {
        private int arrived;
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<ProductionOrder>().Any(entry => entry.State == EntityState.Modified && entry.Entity.Status == ProductionOrderStatus.PendingCommercialReview))
            {
                if (Interlocked.Increment(ref arrived) == 2) ready.TrySetResult();
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            }
            return result;
        }
    }
}
