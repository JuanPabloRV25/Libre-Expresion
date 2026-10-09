using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NPOI.XSSF.UserModel;
using Portal.Domain.Commercial.ProductionOrders;
using Portal.Domain.Permissions;
using Portal.Infrastructure.Identity;
using Portal.Infrastructure.Persistence;
using Portal.Infrastructure.Persistence.Seeding;

namespace Portal.IntegrationTests;

public sealed partial class CommercialProductionOrderEndpointsTests
{
    private const string Password = "Commercial!42";

    [Fact]
    public async Task Superadmin_can_run_the_complete_commercial_and_production_workflow()
    {
        await using var factory = new PortalApiFactory();
        var user = await SeedSuperadminAsync(factory);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(client, user.Document)).StatusCode);

        var unfilteredList = await client.GetAsync("/api/commercial/production-orders");
        Assert.Equal(HttpStatusCode.OK, unfilteredList.StatusCode);

        var create = await SendWithCsrfAsync(client, HttpMethod.Post, "/api/commercial/production-orders", CommercialPayload());
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        var orderId = created.GetProperty("id").GetGuid();
        Assert.Equal("draft", created.GetProperty("status").GetString());
        Assert.StartsWith("OP-", created.GetProperty("code").GetString());
        Assert.Equal(JsonValueKind.Null, created.GetProperty("commercial").GetProperty("customerOrderNumber").ValueKind);
        Assert.Contains("editCommercial", created.GetProperty("allowedActions").EnumerateArray().Select(item => item.GetString()));
        var version = created.GetProperty("version").GetInt32();

        var reviewerDocument = await CreateUserInExistingRoleAsync(factory, DatabaseSeeder.CommercialAssistantRoleName, "Auxiliar");
        await CompleteDocumentChecklistAsync(factory, orderId);

        var submitForReview = await SendWithCsrfAsync(client, HttpMethod.Post, $"/api/commercial/production-orders/{orderId}/submit-for-review", new { version });
        Assert.Equal(HttpStatusCode.OK, submitForReview.StatusCode);
        var pendingReview = await submitForReview.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("pendingCommercialReview", pendingReview.GetProperty("status").GetString());
        version = pendingReview.GetProperty("version").GetInt32();

        using var reviewerClient = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(reviewerClient, reviewerDocument)).StatusCode);
        var missingOrderNumber = await SendWithCsrfAsync(reviewerClient, HttpMethod.Post, $"/api/commercial/production-orders/{orderId}/submit", new { version });
        Assert.Equal(HttpStatusCode.BadRequest, missingOrderNumber.StatusCode);
        var missingOrderNumberError = await missingOrderNumber.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(missingOrderNumberError.GetProperty("errors").TryGetProperty("customerOrderNumber", out _));

        var submit = await SendWithCsrfAsync(reviewerClient, HttpMethod.Post, $"/api/commercial/production-orders/{orderId}/submit", new { version, customerOrderNumber = "PED-2048" });
        Assert.True(submit.IsSuccessStatusCode, $"{submit.StatusCode}: {await submit.Content.ReadAsStringAsync()} (created version {version})");
        var submitted = await submit.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("readyForProduction", submitted.GetProperty("status").GetString());
        Assert.Equal("PED-2048", submitted.GetProperty("commercial").GetProperty("customerOrderNumber").GetString());
        using (var reportScope = factory.Services.CreateScope())
        {
            var context = reportScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var captured = Assert.Single(await context.OpReportRecords.Where(r => r.ProductionOrderId == orderId).ToArrayAsync());
            Assert.Equal("PED-2048", captured.Number);
            Assert.Equal(submitted.GetProperty("version").GetInt32(), captured.OrderVersion);
        }
        version = submitted.GetProperty("version").GetInt32();

        var receive = await SendWithCsrfAsync(client, HttpMethod.Post, $"/api/commercial/production-orders/{orderId}/receive", new { version });
        Assert.Equal(HttpStatusCode.OK, receive.StatusCode);
        var received = await receive.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("inProduction", received.GetProperty("status").GetString());
        version = received.GetProperty("version").GetInt32();
        var materialId = received.GetProperty("materials")[0].GetProperty("id").GetGuid();
        var printLineId = received.GetProperty("printLines")[0].GetProperty("id").GetGuid();
        var finishId = received.GetProperty("finishes")[0].GetProperty("id").GetGuid();

        var updateProduction = await SendWithCsrfAsync(client, HttpMethod.Put, $"/api/commercial/production-orders/{orderId}/production", new
        {
            version,
            planningDate = "2026-10-01",
            planningManager = "Responsable de planeación",
            materialCutDate = "2026-10-02",
            cuttingManager = "Responsable de corte",
            qualityReviewDate = "2026-10-03T15:00:00Z",
            qualityReviewer = "Responsable de calidad",
            qualityApproved = true,
            qualityNotes = "Producto conforme.",
            materials = new[] { new { id = materialId, sheetSize = "70 x 100", sheetQuantity = 50m, totalCutQuantity = 1_000m, conformingQuantity = 990m, nonConformingQuantity = 10m } },
            printLines = new[] { new { id = printLineId, machine = "Offset 1", mounting = "Montaje A", shotsToProcess = 1_000m, conformingQuantity = 990m, nonConformingQuantity = 10m } },
            finishes = new[] { new { id = finishId, totalProcessed = 1_000m, conformingQuantity = 990m, nonConformingQuantity = 10m } },
        });
        Assert.Equal(HttpStatusCode.OK, updateProduction.StatusCode);
        var production = await updateProduction.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Responsable de calidad", production.GetProperty("production").GetProperty("qualityReviewer").GetString());
        version = production.GetProperty("version").GetInt32();

        var complete = await SendWithCsrfAsync(client, HttpMethod.Post, $"/api/commercial/production-orders/{orderId}/complete", new { version });
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);
        var completed = await complete.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("completed", completed.GetProperty("status").GetString());
        Assert.True(completed.GetProperty("history").GetArrayLength() >= 4);

        var duplicate = await SendWithCsrfAsync(client, HttpMethod.Post, $"/api/commercial/production-orders/{orderId}/duplicate", new { });
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        var copy = await duplicate.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("draft", copy.GetProperty("status").GetString());
        Assert.Equal(orderId, copy.GetProperty("sourceOrderId").GetGuid());
        Assert.Equal(JsonValueKind.Null, copy.GetProperty("commercial").GetProperty("customerOrderNumber").ValueKind);
        Assert.Equal(JsonValueKind.Null, copy.GetProperty("production").GetProperty("qualityReviewer").ValueKind);
        Assert.Equal(JsonValueKind.Null, copy.GetProperty("materials")[0].GetProperty("sheetSize").ValueKind);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(2, await db.Set<ProductionOrder>().CountAsync());
        Assert.Contains(await db.AuditEvents.ToArrayAsync(), item => item.Action == "commercial.production_order.completed");
    }

    [Fact]
    public async Task Quotation_preview_does_not_create_an_order_and_confirmation_persists_the_reviewed_fields()
    {
        await using var factory = new PortalApiFactory();
        var user = await SeedSuperadminAsync(factory);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(client, user.Document)).StatusCode);
        var quotation = CreateQuotationWorkbook();

        var preview = await SendQuotationAsync(client, "/api/commercial/production-orders/quotation-preview", quotation);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);

        await using (var previewScope = factory.Services.CreateAsyncScope())
        {
            var previewDb = previewScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(0, await previewDb.Set<ProductionOrder>().CountAsync());
        }

        var commercial = new
        {
            quotationNumber = "99999-1",
            clientName = "CLIENTE FICTICIO SAS",
            productName = "PRODUCTO FICTICIO",
            quantity = 1_000m,
            unitValue = 10m,
            cityCountry = "CIUDAD FICTICIA",
            address = "CALLE FICTICIA 123",
            workType = "unspecified",
            printColorProof = false,
            dieType = "none",
            openSize = "20CMX10CM",
            observations = "PEGADO 4 PUNTAS. NOTA: 2 ENTREGAS EN EL MES",
            partialDelivery = true,
            partialDeliveryQuantity = 500m,
            qualityCertificateMode = "none",
            technicalSheetMode = "none",
            materials = new[] { new { material = "CARTULINA FICTICIA", weight = "30", caliber = "", optionalSpecifications = "" } },
            printLines = new[] { new { product = "PRODUCTO FICTICIO", inks = "1 X 0", process = "PANTONE", specials = "" } },
            finishes = Array.Empty<object>(),
        };
        var createdResponse = await SendQuotationAsync(
            client,
            "/api/commercial/production-orders/import-quotation",
            quotation,
            commercial);
        Assert.True(
            createdResponse.StatusCode == HttpStatusCode.Created,
            $"{createdResponse.StatusCode}: {await createdResponse.Content.ReadAsStringAsync()}");
        var created = await createdResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("99999-1", created.GetProperty("commercial").GetProperty("quotationNumber").GetString());
        Assert.Equal("CLIENTE FICTICIO SAS", created.GetProperty("commercial").GetProperty("clientName").GetString());
        Assert.Equal(1_000m, created.GetProperty("commercial").GetProperty("quantity").GetDecimal());
        Assert.True(created.GetProperty("commercial").GetProperty("partialDelivery").GetBoolean());
        Assert.Equal("30", created.GetProperty("materials")[0].GetProperty("weight").GetString());

        await using var confirmationScope = factory.Services.CreateAsyncScope();
        var confirmationDb = confirmationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await confirmationDb.Set<ProductionOrder>().CountAsync());
    }

    [Fact]
    public async Task Draft_can_be_discarded_when_returning_without_submitting_it()
    {
        await using var factory = new PortalApiFactory();
        var user = await SeedSuperadminAsync(factory);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(client, user.Document)).StatusCode);

        var quotation = CreateQuotationWorkbook();
        var createdResponse = await SendQuotationAsync(
            client,
            "/api/commercial/production-orders/import-quotation",
            quotation,
            CommercialPayload());
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var created = await createdResponse.Content.ReadFromJsonAsync<JsonElement>();
        var orderId = created.GetProperty("id").GetGuid();
        var version = created.GetProperty("version").GetInt32();
        Assert.Contains("discard", created.GetProperty("allowedActions").EnumerateArray().Select(item => item.GetString()));

        var discarded = await SendWithCsrfAsync(
            client,
            HttpMethod.Delete,
            $"/api/commercial/production-orders/{orderId}/draft?version={version}",
            new { });
        Assert.True(
            discarded.StatusCode == HttpStatusCode.OK,
            $"{discarded.StatusCode}: {await discarded.Content.ReadAsStringAsync()}");
        Assert.True((await discarded.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("discarded").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/commercial/production-orders/{orderId}")).StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(0, await db.Set<ProductionOrder>().CountAsync());
        Assert.Equal(0, await db.Set<ProductionOrderDocument>().CountAsync());
    }

    [Fact]
    public async Task Importing_another_op_groups_both_orders_and_exposes_bidirectional_navigation()
    {
        await using var factory = new PortalApiFactory();
        var user = await SeedSuperadminAsync(factory);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(client, user.Document)).StatusCode);
        var quotation = CreateQuotationWorkbook();
        var commercial = CommercialPayload();

        var firstResponse = await SendQuotationAsync(client, "/api/commercial/production-orders/import-quotation", quotation, commercial);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        var first = await firstResponse.Content.ReadFromJsonAsync<JsonElement>();
        var firstId = first.GetProperty("id").GetGuid();

        var secondResponse = await SendQuotationAsync(client, "/api/commercial/production-orders/import-quotation", quotation, commercial, firstId);
        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);
        var second = await secondResponse.Content.ReadFromJsonAsync<JsonElement>();
        var secondId = second.GetProperty("id").GetGuid();

        Assert.Equal(first.GetProperty("operationGroupId").GetGuid(), second.GetProperty("operationGroupId").GetGuid());
        Assert.Equal(firstId, second.GetProperty("relatedOrders")[0].GetProperty("id").GetGuid());

        var refreshedFirst = await client.GetFromJsonAsync<JsonElement>($"/api/commercial/production-orders/{firstId}");
        Assert.Equal(secondId, refreshedFirst.GetProperty("relatedOrders")[0].GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Document_choices_remain_persisted_and_isolated_when_navigating_between_related_orders()
    {
        await using var factory = new PortalApiFactory();
        var user = await SeedSuperadminAsync(factory);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(client, user.Document)).StatusCode);
        var quotation = CreateQuotationWorkbook();

        var firstResponse = await SendQuotationAsync(client, "/api/commercial/production-orders/import-quotation", quotation, CommercialPayload());
        var first = await firstResponse.Content.ReadFromJsonAsync<JsonElement>();
        var firstId = first.GetProperty("id").GetGuid();
        var firstVersion = first.GetProperty("version").GetInt32();

        var secondResponse = await SendQuotationAsync(client, "/api/commercial/production-orders/import-quotation", quotation, CommercialPayload(), firstId);
        var second = await secondResponse.Content.ReadFromJsonAsync<JsonElement>();
        var secondId = second.GetProperty("id").GetGuid();
        var currentFirst = await client.GetFromJsonAsync<JsonElement>($"/api/commercial/production-orders/{firstId}");
        firstVersion = currentFirst.GetProperty("version").GetInt32();

        var designNotRequired = await SendWithCsrfAsync(
            client,
            HttpMethod.Put,
            $"/api/commercial/production-orders/{firstId}/documents/design/applicability",
            new { version = firstVersion, notApplicable = true });
        Assert.Equal(HttpStatusCode.OK, designNotRequired.StatusCode);
        var persistedAfterDesign = await client.GetFromJsonAsync<JsonElement>($"/api/commercial/production-orders/{firstId}");
        firstVersion = persistedAfterDesign.GetProperty("version").GetInt32();

        var purchaseOrder = await SendDocumentAsync(
            client,
            $"/api/commercial/production-orders/{firstId}/documents/purchaseOrder",
            firstVersion,
            "%PDF-1.4\n%%EOF"u8.ToArray(),
            "Orden-de-compra.pdf");
        Assert.True(
            purchaseOrder.StatusCode == HttpStatusCode.OK,
            $"{purchaseOrder.StatusCode}: {await purchaseOrder.Content.ReadAsStringAsync()} (version {firstVersion})");

        var refreshedSecond = await client.GetFromJsonAsync<JsonElement>($"/api/commercial/production-orders/{secondId}");
        Assert.Equal("pending", refreshedSecond.GetProperty("checklist").GetProperty("purchaseOrder").GetString());
        Assert.Equal("pending", refreshedSecond.GetProperty("checklist").GetProperty("design").GetString());

        using var freshClient = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(freshClient, user.Document)).StatusCode);
        var persistedFirst = await freshClient.GetFromJsonAsync<JsonElement>($"/api/commercial/production-orders/{firstId}");
        Assert.Equal("attached", persistedFirst.GetProperty("checklist").GetProperty("purchaseOrder").GetString());
        Assert.Equal("notApplicable", persistedFirst.GetProperty("checklist").GetProperty("design").GetString());
        Assert.Contains(
            persistedFirst.GetProperty("documents").EnumerateArray(),
            document => document.GetProperty("type").GetString() == "purchaseOrder"
                && document.GetProperty("originalFileName").GetString() == "Orden-de-compra.pdf");
    }

    [Fact]
    public async Task Commercial_production_and_standard_profiles_enforce_the_permission_boundaries()
    {
        await using var factory = new PortalApiFactory();
        await SeedSuperadminAsync(factory);
        var commercialDocument = await CreatePermissionedUserAsync(factory, "Comercial", [
            CommercialPermissionCodes.OrdersView,
            CommercialPermissionCodes.OrdersCreate,
            CommercialPermissionCodes.OrdersEditCommercial,
            CommercialPermissionCodes.OrdersSubmitForReview,
            CommercialPermissionCodes.OrdersDuplicate,
        ]);
        var productionDocument = await CreatePermissionedUserAsync(factory, "Producción", [
            CommercialPermissionCodes.OrdersView,
            CommercialPermissionCodes.OrdersViewProduction,
            CommercialPermissionCodes.OrdersEditProduction,
            CommercialPermissionCodes.OrdersComplete,
        ]);
        var standardDocument = await CreatePermissionedUserAsync(factory, "Usuario interno", []);
        var assistantDocument = await CreateUserInExistingRoleAsync(factory, DatabaseSeeder.CommercialAssistantRoleName, "Auxiliar");

        using var commercialClient = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        using var productionClient = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        using var standardClient = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        using var assistantClient = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(commercialClient, commercialDocument)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(productionClient, productionDocument)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(standardClient, standardDocument)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(assistantClient, assistantDocument)).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await standardClient.GetAsync("/api/commercial/production-orders")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await SendWithCsrfAsync(productionClient, HttpMethod.Post, "/api/commercial/production-orders", CommercialPayload())).StatusCode);

        var create = await SendWithCsrfAsync(commercialClient, HttpMethod.Post, "/api/commercial/production-orders", CommercialPayload());
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        var orderId = created.GetProperty("id").GetGuid();
        var version = created.GetProperty("version").GetInt32();
        await CompleteDocumentChecklistAsync(factory, orderId);

        var submit = await SendWithCsrfAsync(
            commercialClient,
            HttpMethod.Post,
            $"/api/commercial/production-orders/{orderId}/submit-for-review",
            new { version });
        Assert.Equal(HttpStatusCode.OK, submit.StatusCode);
        version = (await submit.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("version").GetInt32();

        var approve = await SendWithCsrfAsync(
            assistantClient,
            HttpMethod.Post,
            $"/api/commercial/production-orders/{orderId}/submit",
            new { version, customerOrderNumber = "PED-ROLE-2048" });
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        version = (await approve.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("version").GetInt32();

        Assert.Equal(HttpStatusCode.OK, (await productionClient.GetAsync($"/api/commercial/production-orders/{orderId}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await SendWithCsrfAsync(productionClient, HttpMethod.Post, $"/api/commercial/production-orders/{orderId}/duplicate", new { })).StatusCode);

        var receive = await SendWithCsrfAsync(
            productionClient,
            HttpMethod.Post,
            $"/api/commercial/production-orders/{orderId}/receive",
            new { version });
        Assert.Equal(HttpStatusCode.OK, receive.StatusCode);
        var received = await receive.Content.ReadFromJsonAsync<JsonElement>();
        version = received.GetProperty("version").GetInt32();

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await SendWithCsrfAsync(
                commercialClient,
                HttpMethod.Put,
                $"/api/commercial/production-orders/{orderId}/production",
                new { version })).StatusCode);

        var updateProduction = await SendWithCsrfAsync(
            productionClient,
            HttpMethod.Put,
            $"/api/commercial/production-orders/{orderId}/production",
            new
            {
                version,
                qualityReviewDate = "2026-10-03T15:00:00Z",
                qualityReviewer = "Control de calidad",
                qualityApproved = true,
            });
        Assert.Equal(HttpStatusCode.OK, updateProduction.StatusCode);
        version = (await updateProduction.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("version").GetInt32();

        Assert.Equal(
            HttpStatusCode.OK,
            (await SendWithCsrfAsync(
                productionClient,
                HttpMethod.Post,
                $"/api/commercial/production-orders/{orderId}/complete",
                new { version })).StatusCode);
    }

    [Fact]
    public async Task Stale_commercial_update_is_rejected_with_a_concurrency_conflict()
    {
        await using var factory = new PortalApiFactory();
        var user = await SeedSuperadminAsync(factory);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(client, user.Document)).StatusCode);

        var create = await SendWithCsrfAsync(client, HttpMethod.Post, "/api/commercial/production-orders", CommercialPayload());
        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        var orderId = created.GetProperty("id").GetGuid();
        var staleVersion = created.GetProperty("version").GetInt32();

        var firstUpdate = await SendWithCsrfAsync(
            client,
            HttpMethod.Put,
            $"/api/commercial/production-orders/{orderId}/commercial",
            CommercialPayload(staleVersion, "Cliente actualizado"));
        Assert.True(
            firstUpdate.IsSuccessStatusCode,
            $"{firstUpdate.StatusCode}: {await firstUpdate.Content.ReadAsStringAsync()} (version {staleVersion})");

        var staleUpdate = await SendWithCsrfAsync(
            client,
            HttpMethod.Put,
            $"/api/commercial/production-orders/{orderId}/commercial",
            CommercialPayload(staleVersion, "Cliente sobrescrito"));
        Assert.Equal(HttpStatusCode.Conflict, staleUpdate.StatusCode);
        Assert.Equal(
            "production_order_version_conflict",
            (await staleUpdate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Submission_returns_field_errors_for_an_incomplete_draft()
    {
        await using var factory = new PortalApiFactory();
        var user = await SeedSuperadminAsync(factory);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await LoginAsync(client, user.Document);

        var create = await SendWithCsrfAsync(client, HttpMethod.Post, "/api/commercial/production-orders", new
        {
            workType = "unspecified",
            dieType = "none",
            qualityCertificateMode = "none",
            technicalSheetMode = "none",
        });
        var order = await create.Content.ReadFromJsonAsync<JsonElement>();
        var submit = await SendWithCsrfAsync(client, HttpMethod.Post, $"/api/commercial/production-orders/{order.GetProperty("id").GetGuid()}/submit-for-review", new
        {
            version = order.GetProperty("version").GetInt32(),
        });

        Assert.Equal(HttpStatusCode.BadRequest, submit.StatusCode);
        var error = await submit.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("production_order_validation", error.GetProperty("code").GetString());
        Assert.True(error.GetProperty("errors").TryGetProperty("clientName", out _));
        Assert.True(error.GetProperty("errors").TryGetProperty("deliveryDate", out _));
    }

    private static object CommercialPayload(int? version = null, string clientName = "Cliente de prueba") => new
    {
        version,
        customerOrderNumber = "PED-2048",
        deliveryDate = "2026-10-15",
        clientName,
        productName = "Caja plegadiza",
        referenceNumber = "REF-01",
        clientPurchaseOrder = "OC-918",
        quantity = 1_000m,
        unitValue = 1_250m,
        cityCountry = "Bogotá, Colombia",
        address = "Dirección opcional",
        workType = "new",
        printColorProof = true,
        dieType = "new",
        openSize = "40 x 30 cm",
        closedSize = "20 x 15 x 5 cm",
        observations = "Observación comercial",
        additionalSpecifications = "Especificación opcional",
        receptionContact = "Cliente receptor",
        deliveryAddress = "Bodega principal",
        receptionSchedule = "Lunes a viernes, 8 a 17",
        partialDelivery = false,
        legalContractRequirements = "Sin requisitos adicionales",
        qualityCertificateMode = "digital",
        technicalSheetMode = "digital",
        materials = new[] { new { material = "Cartulina", weight = "300 gr", caliber = "18", optionalSpecifications = "Blanca" } },
        printLines = new[] { new { product = "Caja", inks = "CMYK", process = "Offset", specials = "Barniz" } },
        finishes = new[] { new { specification = "Troquelado", front = true, back = false, reserve = false } },
    };

    private static async Task<string> CreatePermissionedUserAsync(
        PortalApiFactory factory,
        string rolePrefix,
        IReadOnlyCollection<string> permissionCodes)
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
            Name = $"{rolePrefix} {Guid.NewGuid():N}",
            Description = $"Rol de prueba para {rolePrefix}.",
            IsActive = true,
            IsSystem = false,
            CreatedAt = now,
            UpdatedAt = now,
        };
        Assert.True((await roleManager.CreateAsync(role)).Succeeded);

        var permissions = await context.Permissions
            .Where(permission => permissionCodes.Contains(permission.Code))
            .ToArrayAsync();
        Assert.Equal(permissionCodes.Count, permissions.Length);
        context.RolePermissions.AddRange(permissions.Select(permission => new RolePermission
        {
            RoleId = role.Id,
            PermissionId = permission.Id,
            AssignedAt = now,
        }));

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = document,
            Email = $"commercial-profile-{Guid.NewGuid():N}@example.test",
            FirstName = rolePrefix,
            LastName = "Prueba",
            IsActive = true,
            MustChangePassword = false,
            CreatedAt = now,
            UpdatedAt = now,
        };
        Assert.True((await userManager.CreateAsync(user, Password)).Succeeded);
        context.Set<ApplicationUserRole>().Add(new ApplicationUserRole
        {
            UserId = user.Id,
            RoleId = role.Id,
            AssignedAt = now,
        });
        await context.SaveChangesAsync();
        return document;
    }

    private static async Task<string> CreateUserInExistingRoleAsync(
        PortalApiFactory factory,
        string roleName,
        string firstName)
    {
        var document = $"7{Random.Shared.NextInt64(100000000, 999999999)}";
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var role = await roleManager.FindByNameAsync(roleName) ?? throw new InvalidOperationException($"Missing role {roleName}.");
        var now = DateTimeOffset.UtcNow;
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(), UserName = document, Email = $"assistant-{Guid.NewGuid():N}@example.test",
            FirstName = firstName, LastName = "Prueba", IsActive = true, MustChangePassword = false,
            CreatedAt = now, UpdatedAt = now,
        };
        Assert.True((await userManager.CreateAsync(user, Password)).Succeeded);
        context.Set<ApplicationUserRole>().Add(new ApplicationUserRole { UserId = user.Id, RoleId = role.Id, AssignedAt = now });
        await context.SaveChangesAsync();
        return document;
    }

    private static async Task CompleteDocumentChecklistAsync(PortalApiFactory factory, Guid orderId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var order = await context.ProductionOrders.SingleAsync(candidate => candidate.Id == orderId);
        order.PurchaseOrderApplicability = DocumentApplicabilityStatus.NotApplicable;
        order.DesignApplicability = DocumentApplicabilityStatus.NotApplicable;
        context.ProductionOrderDocuments.Add(new ProductionOrderDocument
        {
            Id = Guid.NewGuid(), ProductionOrderId = orderId, Type = ProductionOrderDocumentType.Quotation,
            Applicability = DocumentApplicabilityStatus.Attached, OriginalFileName = "cotizacion.pdf",
            StorageKey = $"tests/{Guid.NewGuid():N}.pdf", ContentType = "application/pdf", Size = 100,
            Sha256 = new string('a', 64), IsActive = true, UploadedByUserId = order.CommercialOwnerUserId,
            UploadedAt = DateTimeOffset.UtcNow,
        });
        await context.SaveChangesAsync();
    }

    private static async Task<(Guid Id, string Document)> SeedSuperadminAsync(PortalApiFactory factory)
    {
        var document = $"9{Random.Shared.NextInt64(100000000, 999999999)}";
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.EnsureCreatedAsync();
        var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
        await seeder.SeedAsync(new DatabaseSeedSettings(document, "Comercial", "Administrador", $"commercial-{Guid.NewGuid():N}@example.test"));
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByNameAsync(document) ?? throw new InvalidOperationException();
        user.PasswordHash = scope.ServiceProvider.GetRequiredService<IPasswordHasher<ApplicationUser>>().HashPassword(user, Password);
        user.MustChangePassword = false;
        Assert.True((await userManager.UpdateAsync(user)).Succeeded);
        return (user.Id, document);
    }

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string document) =>
        SendWithCsrfAsync(client, HttpMethod.Post, "/api/auth/login", new { documentNumber = document, password = Password });

    private static async Task<HttpResponseMessage> SendWithCsrfAsync(HttpClient client, HttpMethod method, string path, object body)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-XSRF-TOKEN", csrf.GetProperty("token").GetString());
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SendQuotationAsync(
        HttpClient client,
        string path,
        byte[] quotation,
        object? commercial = null,
        Guid? relatedOrderId = null)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(quotation);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        form.Add(file, "file", "Cotizacion.xlsx");
        if (commercial is not null)
        {
            form.Add(new StringContent("0", Encoding.UTF8), "itemIndex");
            form.Add(new StringContent("0", Encoding.UTF8), "optionIndex");
            form.Add(new StringContent(JsonSerializer.Serialize(commercial), Encoding.UTF8, "application/json"), "commercial");
            if (relatedOrderId.HasValue)
                form.Add(new StringContent(relatedOrderId.Value.ToString()), "relatedOrderId");
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = form };
        request.Headers.Add("X-XSRF-TOKEN", csrf.GetProperty("token").GetString());
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SendDocumentAsync(
        HttpClient client,
        string path,
        int version,
        byte[] content,
        string fileName)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", fileName);
        form.Add(new StringContent(version.ToString()), "version");
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = form };
        request.Headers.Add("X-XSRF-TOKEN", csrf.GetProperty("token").GetString());
        return await client.SendAsync(request);
    }

    private static byte[] CreateQuotationWorkbook()
    {
        using var workbook = new XSSFWorkbook();
        var sheet = workbook.CreateSheet("Cotización");
        // Synthetic quotation data; no customer records.
        var lines = new[]
        {
            "Cotización N° 99999-1",
            "CLIENTE FICTICIO SAS",
            "Atn: COMPRAS",
            "NIT: 000000000",
            "CALLE FICTICIA 123",
            "CIUDAD FICTICIA",
            "1 C - PLEGADIZA : PRODUCTO FICTICIO: MEDIDA ABIERTA: 20CMX10CM - MATERIAL: CARTULINA FICTICIA CAL 30 - IMPRESION: 1X0 (PANTONE) - TROQUELADO - PEGADO 4 PUNTAS NOTA: 2 ENTREGAS EN EL MES",
            "1.000 $ 10 $ 10.000 $ 1.900 $ 11.900",
            "Condiciones comerciales:",
        };
        for (var index = 0; index < lines.Length; index++)
        {
            sheet.CreateRow(index).CreateCell(0).SetCellValue(lines[index]);
        }
        using var memory = new MemoryStream();
        workbook.Write(memory, leaveOpen: true);
        return memory.ToArray();
    }
}
