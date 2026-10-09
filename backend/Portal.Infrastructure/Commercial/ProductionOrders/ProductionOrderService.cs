using System.Net.Mail;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Portal.Application.Commercial.ProductionOrders;
using Portal.Application.Identity;
using Portal.Domain.Auditing;
using Portal.Domain.Commercial.ProductionOrders;
using Portal.Infrastructure.Identity;
using Portal.Infrastructure.Persistence;

namespace Portal.Infrastructure.Commercial.ProductionOrders;

public sealed class ProductionOrderService(
    ApplicationDbContext dbContext,
    ICurrentUserService currentUserService,
    IQuotationImporter quotationImporter,
    IProductionOrderDocumentStorage documentStorage,
    TimeProvider timeProvider) : IProductionOrderService
{
    private const int MaximumLineCount = 20;

    public async Task<IReadOnlyList<ProductionOrderSummaryDto>> ListAsync(
        ProductionOrderListQuery query,
        CancellationToken cancellationToken = default)
    {
        var currentUser = await RequireCurrentUserAsync(cancellationToken);
        var permissions = currentUser.Permissions;
        var canManage = Has(permissions, CommercialPermissionCodes.OrdersManage);
        var canViewProduction = Has(permissions, CommercialPermissionCodes.OrdersViewProduction);
        var canReview = Has(permissions, CommercialPermissionCodes.OrdersReview);
        var orders = dbContext.Set<ProductionOrder>().AsNoTracking();

        if (!canManage)
        {
            orders = orders.Where(order => order.CommercialOwnerUserId == currentUser.Id
                || (canReview && order.CurrentAssigneeUserId == currentUser.Id)
                || (canViewProduction && (order.Status == ProductionOrderStatus.ReadyForProduction
                    || order.Status == ProductionOrderStatus.InProduction
                    || order.Status == ProductionOrderStatus.Completed
                    || order.Status == ProductionOrderStatus.Cancelled)));
        }

        if (query.AssignedToMe)
        {
            orders = orders.Where(order => order.CurrentAssigneeUserId == currentUser.Id);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim().ToLower();
            orders = orders.Where(order =>
                (order.ClientName != null && order.ClientName.ToLower().Contains(search))
                || (order.ProductName != null && order.ProductName.ToLower().Contains(search))
                || (order.CustomerOrderNumber != null && order.CustomerOrderNumber.ToLower().Contains(search))
                || order.Consecutive.ToString().Contains(search));
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            if (!TryParseStatus(query.Status, out var status))
            {
                return [];
            }

            orders = orders.Where(order => order.Status == status);
        }

        var rows = await orders
            .OrderByDescending(order => order.UpdatedAt)
            .Take(500)
            .ToArrayAsync(cancellationToken);
        var people = await LoadPeopleAsync(
            rows.SelectMany(order => new[] { (Guid?)order.CommercialOwnerUserId, order.ProductionOwnerUserId }),
            cancellationToken);

        return rows.Select(order => ToSummary(
            order,
            people[order.CommercialOwnerUserId],
            order.ProductionOwnerUserId is { } productionOwnerId ? people.GetValueOrDefault(productionOwnerId) : null,
            AllowedActions(order, currentUser))).ToArray();
    }

    public async Task<ProductionOrderOperationResult> GetAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var currentUser = await RequireCurrentUserAsync(cancellationToken);
        var order = await LoadOrderAsync(id, cancellationToken);
        if (order is null)
        {
            return NotFound();
        }

        if (!CanView(order, currentUser))
        {
            return Forbidden();
        }

        return Success(await ToDetailAsync(order, currentUser, cancellationToken));
    }

    public async Task<ProductionOrderOperationResult> CreateAsync(
        SaveCommercialOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        var currentUser = await RequireCurrentUserAsync(cancellationToken);
        var validation = ValidateCommercial(command, false);
        if (validation.Count > 0)
        {
            return Invalid(validation);
        }

        var now = timeProvider.GetUtcNow();
        var orderId = Guid.NewGuid();
        var order = new ProductionOrder
        {
            Id = orderId,
            OperationGroupId = orderId,
            CommercialOwnerUserId = currentUser.Id,
            CreatedByUserId = currentUser.Id,
            LastUpdatedByUserId = currentUser.Id,
            CurrentAssigneeUserId = currentUser.Id,
            CreatedAt = now,
            UpdatedAt = now,
        };
        ApplyCommercial(order, command);
        ReplaceCommercialLines(order, command);
        AddHistory(order, ProductionOrderStatus.Draft, currentUser.Id, "Orden comercial creada.", now);
        dbContext.Set<ProductionOrder>().Add(order);
        AddAudit(order, currentUser.Id, "commercial.production_order.created", now);

        await dbContext.SaveChangesAsync(cancellationToken);
        return Success(await ToDetailAsync(order, currentUser, cancellationToken));
    }

    public async Task<ProductionOrderOperationResult> UpdateCommercialAsync(
        Guid id,
        SaveCommercialOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        var currentUser = await RequireCurrentUserAsync(cancellationToken);
        var order = await LoadOrderAsync(id, cancellationToken);
        if (order is null)
        {
            return NotFound();
        }

        if (!CanEditCommercial(order, currentUser))
        {
            return Forbidden("La información comercial solo puede modificarse en borrador por su responsable.");
        }

        if (!command.Version.HasValue || command.Version.Value != order.Version)
        {
            return VersionConflict();
        }

        var validation = ValidateCommercial(command, false);
        if (validation.Count > 0)
        {
            return Invalid(validation);
        }

        ApplyCommercial(order, command);
        ReplaceCommercialLines(order, command);
        Touch(order, currentUser.Id);
        AddAudit(order, currentUser.Id, "commercial.production_order.commercial_updated", order.UpdatedAt);
        return await SaveAsync(order, currentUser, cancellationToken);
    }

    public Task<QuotationPreviewResult> PreviewQuotationAsync(
        UploadedFileCommand file,
        CancellationToken cancellationToken = default) =>
        quotationImporter.PreviewAsync(file, cancellationToken);

    public async Task<ProductionOrderOperationResult> ImportQuotationAsync(
        UploadedFileCommand file,
        ImportQuotationSelection selection,
        SaveCommercialOrderCommand? commercial,
        Guid? relatedOrderId,
        CancellationToken cancellationToken = default)
    {
        var currentUser = await RequireCurrentUserAsync(cancellationToken);
        if (!Has(currentUser.Permissions, CommercialPermissionCodes.OrdersCreate)
            && !Has(currentUser.Permissions, CommercialPermissionCodes.OrdersManage))
        {
            return Forbidden("No tienes permiso para crear órdenes de producción.");
        }

        var buffered = await BufferAsync(file, cancellationToken);
        var previewResult = await quotationImporter.PreviewAsync(buffered.Command(), cancellationToken);
        if (!previewResult.Success || previewResult.Preview is null)
        {
            return Invalid(new Dictionary<string, string[]> { ["quotation"] = [previewResult.ErrorMessage ?? "No fue posible importar la cotización."] });
        }

        if (!TrySelect(previewResult.Preview, selection, out var item, out var option))
        {
            return Invalid(new Dictionary<string, string[]> { ["selection"] = ["Selecciona un producto y una opción de cantidad válidos."] });
        }

        ProductionOrder? relatedOrder = null;
        if (relatedOrderId.HasValue)
        {
            relatedOrder = await LoadOrderAsync(relatedOrderId.Value, cancellationToken);
            if (relatedOrder is null) return Invalid(new Dictionary<string, string[]> { ["relatedOrderId"] = ["La OP anterior ya no existe."] });
            if (!CanView(relatedOrder, currentUser)) return Forbidden("No tienes acceso a la OP relacionada.");
        }

        var now = timeProvider.GetUtcNow();
        var orderId = Guid.NewGuid();
        var order = new ProductionOrder
        {
            Id = orderId,
            OperationGroupId = relatedOrder?.OperationGroupId ?? orderId,
            Status = ProductionOrderStatus.Draft,
            CommercialOwnerUserId = currentUser.Id,
            CreatedByUserId = currentUser.Id,
            LastUpdatedByUserId = currentUser.Id,
            CurrentAssigneeUserId = currentUser.Id,
            QuotationNumber = Normalize(previewResult.Preview.QuotationNumber),
            ClientName = Normalize(previewResult.Preview.ClientName),
            ProductName = Normalize(item.ProductName),
            ReferenceNumber = ExtractReference(item.Description),
            Quantity = option.Quantity,
            UnitValue = option.UnitValue,
            CityCountry = Normalize(previewResult.Preview.CityCountry),
            Address = Normalize(previewResult.Preview.Address),
            OpenSize = Normalize(item.OpenSize),
            WorkType = CommercialWorkType.Unspecified,
            DieType = DieType.None,
            CreatedAt = now,
            UpdatedAt = now,
        };
        ApplyImportedSelection(order, previewResult.Preview, item, option);
        AddImportedLines(order, item);

        if (commercial is not null)
        {
            var validation = ValidateCommercial(commercial, false);
            if (validation.Count > 0) return Invalid(validation);
            ApplyCommercial(order, commercial);
            ReplaceCommercialLines(order, commercial);
        }

        AddHistory(order, order.Status, currentUser.Id, "Borrador creado desde cotización importada.", now);

        StoredDocument? stored = null;
        try
        {
            stored = await documentStorage.SaveAsync(order.Id, buffered.Command(), cancellationToken);
            var document = CreateDocument(order, ProductionOrderDocumentType.Quotation, stored, currentUser.Id, now);
            order.Documents.Add(document);
            order.Imports.Add(CreateImport(order, document, previewResult.Preview, selection, currentUser.Id, now));
            dbContext.Set<ProductionOrder>().Add(order);
            AddAudit(order, currentUser.Id, "commercial.production_order.quotation_imported", now,
                new { previewResult.Preview.Format, selection.ItemIndex, selection.OptionIndex, stored.Sha256 });
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            if (stored is not null) await documentStorage.DeleteAsync(stored.StorageKey, cancellationToken);
            throw;
        }

        return Success(await ToDetailAsync(order, currentUser, cancellationToken));
    }

    public async Task<ProductionOrderOperationResult> ReplaceQuotationAsync(
        Guid id,
        int version,
        UploadedFileCommand file,
        ImportQuotationSelection selection,
        CancellationToken cancellationToken = default)
    {
        var currentUser = await RequireCurrentUserAsync(cancellationToken);
        var order = await LoadOrderAsync(id, cancellationToken);
        if (order is null) return NotFound();
        if (!CanEditCommercial(order, currentUser)) return Forbidden("La cotización solo puede reemplazarse en borrador o corrección por su responsable.");
        if (version != order.Version) return VersionConflict();

        var buffered = await BufferAsync(file, cancellationToken);
        var previewResult = await quotationImporter.PreviewAsync(buffered.Command(), cancellationToken);
        if (!previewResult.Success || previewResult.Preview is null)
            return Invalid(new Dictionary<string, string[]> { ["quotation"] = [previewResult.ErrorMessage ?? "No fue posible importar la cotización."] });
        if (!TrySelect(previewResult.Preview, selection, out var item, out var option))
            return Invalid(new Dictionary<string, string[]> { ["selection"] = ["Selecciona un producto y una opción de cantidad válidos."] });

        var totalWithoutOldQuote = order.Documents.Where(document => document.IsActive && document.Type != ProductionOrderDocumentType.Quotation).Sum(document => document.Size);
        if (totalWithoutOldQuote + buffered.Bytes.LongLength > ProductionOrderFileValidator.MaximumOrderSize)
            return Invalid(new Dictionary<string, string[]> { ["quotation"] = ["Los documentos de la orden superarían el límite acumulado de 100 MB."] });

        var now = timeProvider.GetUtcNow();
        var stored = await documentStorage.SaveAsync(order.Id, buffered.Command(), cancellationToken);
        try
        {
            foreach (var previous in order.Documents.Where(document => document.IsActive && document.Type == ProductionOrderDocumentType.Quotation))
            {
                previous.IsActive = false;
                previous.SupersededAt = now;
            }
            var document = CreateDocument(order, ProductionOrderDocumentType.Quotation, stored, currentUser.Id, now);
            var import = CreateImport(order, document, previewResult.Preview, selection, currentUser.Id, now);
            order.Documents.Add(document);
            order.Imports.Add(import);
            dbContext.Set<ProductionOrderDocument>().Add(document);
            dbContext.Set<ProductionOrderImport>().Add(import);
            ApplyImportedSelection(order, previewResult.Preview, item, option);
            ReplaceImportedLines(order, item);
            Touch(order, currentUser.Id, now);
            AddAudit(order, currentUser.Id, "commercial.production_order.quotation_replaced", now, new { stored.Sha256 });
            return await SaveAsync(order, currentUser, cancellationToken);
        }
        catch
        {
            await documentStorage.DeleteAsync(stored.StorageKey, cancellationToken);
            throw;
        }
    }

    public async Task<ProductionOrderOperationResult> AddDocumentAsync(
        Guid id,
        int version,
        string type,
        UploadedFileCommand file,
        CancellationToken cancellationToken = default)
    {
        var currentUser = await RequireCurrentUserAsync(cancellationToken);
        var order = await LoadOrderAsync(id, cancellationToken);
        if (order is null) return NotFound();
        if (!CanEditCommercial(order, currentUser)) return Forbidden("Los documentos solo pueden modificarse en borrador o corrección.");
        if (version != order.Version) return VersionConflict();
        if (!TryDocumentType(type, out var documentType) || documentType == ProductionOrderDocumentType.Quotation)
            return Invalid(new Dictionary<string, string[]> { ["type"] = ["El tipo de documento no es válido."] });

        var buffered = await BufferAsync(file, cancellationToken);
        var validation = ProductionOrderFileValidator.ValidateDocument(file.FileName, buffered.Bytes.LongLength, buffered.Bytes.AsSpan(0, Math.Min(16, buffered.Bytes.Length)));
        if (validation is not null) return Invalid(new Dictionary<string, string[]> { ["file"] = [validation] });
        var activeSize = order.Documents.Where(document => document.IsActive).Sum(document => document.Size);
        if (activeSize + buffered.Bytes.LongLength > ProductionOrderFileValidator.MaximumOrderSize)
            return Invalid(new Dictionary<string, string[]> { ["file"] = ["Los documentos de la orden superarían el límite acumulado de 100 MB."] });

        var now = timeProvider.GetUtcNow();
        var stored = await documentStorage.SaveAsync(order.Id, buffered.Command(), cancellationToken);
        try
        {
            if (documentType == ProductionOrderDocumentType.PurchaseOrder)
            {
                foreach (var previous in order.Documents.Where(document => document.IsActive && document.Type == documentType))
                {
                    previous.IsActive = false;
                    previous.SupersededAt = now;
                }
                order.PurchaseOrderApplicability = DocumentApplicabilityStatus.Attached;
            }
            else
            {
                order.DesignApplicability = DocumentApplicabilityStatus.Attached;
            }
            var document = CreateDocument(order, documentType, stored, currentUser.Id, now);
            order.Documents.Add(document);
            dbContext.Set<ProductionOrderDocument>().Add(document);
            Touch(order, currentUser.Id, now);
            AddAudit(order, currentUser.Id, "commercial.production_order.document_added", now, new { Type = documentType.ToString(), stored.Sha256 });
            return await SaveAsync(order, currentUser, cancellationToken);
        }
        catch
        {
            await documentStorage.DeleteAsync(stored.StorageKey, cancellationToken);
            throw;
        }
    }

    public async Task<ProductionOrderOperationResult> SetDocumentApplicabilityAsync(
        Guid id,
        int version,
        string type,
        bool notApplicable,
        CancellationToken cancellationToken = default)
    {
        var currentUser = await RequireCurrentUserAsync(cancellationToken);
        var order = await LoadOrderAsync(id, cancellationToken);
        if (order is null) return NotFound();
        if (!CanEditCommercial(order, currentUser)) return Forbidden("El checklist solo puede modificarse en borrador o corrección.");
        if (version != order.Version) return VersionConflict();
        if (!TryDocumentType(type, out var documentType) || documentType == ProductionOrderDocumentType.Quotation)
            return Invalid(new Dictionary<string, string[]> { ["type"] = ["El tipo de documento no es válido."] });

        var status = notApplicable ? DocumentApplicabilityStatus.NotApplicable : DocumentApplicabilityStatus.Pending;
        if (documentType == ProductionOrderDocumentType.PurchaseOrder) order.PurchaseOrderApplicability = status;
        else order.DesignApplicability = status;
        Touch(order, currentUser.Id);
        AddAudit(order, currentUser.Id, "commercial.production_order.document_applicability_changed", order.UpdatedAt,
            new { Type = documentType.ToString(), Applicability = status.ToString() });
        return await SaveAsync(order, currentUser, cancellationToken);
    }

    public async Task<DocumentDownloadResult> DownloadDocumentAsync(Guid id, Guid documentId, CancellationToken cancellationToken = default)
    {
        var currentUser = await RequireCurrentUserAsync(cancellationToken);
        var order = await LoadOrderAsync(id, cancellationToken);
        if (order is null || !CanView(order, currentUser))
            return new(false, ErrorCode: "document_not_found", ErrorMessage: "El documento no existe o no está disponible.");
        var document = order.Documents.SingleOrDefault(candidate => candidate.Id == documentId && candidate.IsActive);
        if (document is null) return new(false, ErrorCode: "document_not_found", ErrorMessage: "El documento no existe o no está disponible.");
        return new(true, await documentStorage.OpenReadAsync(document.StorageKey, cancellationToken), document.ContentType, document.OriginalFileName);
    }

    public async Task<ProductionOrderOperationResult> DeleteDocumentAsync(Guid id, Guid documentId, int version, CancellationToken cancellationToken = default)
    {
        var currentUser = await RequireCurrentUserAsync(cancellationToken);
        var order = await LoadOrderAsync(id, cancellationToken);
        if (order is null) return NotFound();
        if (!CanEditCommercial(order, currentUser)) return Forbidden("Los documentos solo pueden eliminarse en borrador o corrección.");
        if (version != order.Version) return VersionConflict();
        var document = order.Documents.SingleOrDefault(candidate => candidate.Id == documentId && candidate.IsActive);
        if (document is null || document.Type == ProductionOrderDocumentType.Quotation)
            return Invalid(new Dictionary<string, string[]> { ["document"] = ["El documento no existe o la cotización debe reemplazarse, no eliminarse."] });
        document.IsActive = false;
        document.SupersededAt = timeProvider.GetUtcNow();
        if (!order.Documents.Any(candidate => candidate.IsActive && candidate.Id != document.Id && candidate.Type == document.Type))
        {
            if (document.Type == ProductionOrderDocumentType.PurchaseOrder) order.PurchaseOrderApplicability = DocumentApplicabilityStatus.Pending;
            else order.DesignApplicability = DocumentApplicabilityStatus.Pending;
        }
        Touch(order, currentUser.Id, document.SupersededAt);
        AddAudit(order, currentUser.Id, "commercial.production_order.document_removed", order.UpdatedAt, new { Type = document.Type.ToString() });
        return await SaveAsync(order, currentUser, cancellationToken);
    }

    public async Task<ProductionOrderOperationResult> DiscardDraftAsync(
        Guid id,
        int version,
        CancellationToken cancellationToken = default)
    {
        var currentUser = await RequireCurrentUserAsync(cancellationToken);
        var order = await LoadOrderAsync(id, cancellationToken);
        if (order is null)
        {
            return NotFound();
        }

        if (order.Status != ProductionOrderStatus.Draft || !CanEditCommercial(order, currentUser))
        {
            return Forbidden("Solo el responsable puede descartar una OP que continúa en borrador comercial.");
        }

        if (version != order.Version)
        {
            return VersionConflict();
        }

        var storageKeys = order.Documents
            .Select(document => document.StorageKey)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        AddAudit(order, currentUser.Id, "commercial.production_order.draft_discarded", timeProvider.GetUtcNow());
        // La importación referencia obligatoriamente al documento de origen
        // con DeleteBehavior.Restrict. Se marca primero para eliminación y así
        // EF no intenta romper esa relación requerida al retirar la OP.
        dbContext.Set<ProductionOrderImport>().RemoveRange(order.Imports);
        dbContext.Set<ProductionOrder>().Remove(order);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return VersionConflict();
        }

        foreach (var storageKey in storageKeys)
        {
            try
            {
                await documentStorage.DeleteAsync(storageKey, cancellationToken);
            }
            catch (IOException)
            {
                // El registro ya no es accesible. Un residuo físico puede ser
                // retirado por mantenimiento sin revivir el borrador descartado.
            }
        }

        return new ProductionOrderOperationResult(ProductionOrderOperationStatus.Success);
    }

    public async Task<ProductionOrderReviewersResult> GetReviewersAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var currentUser = await RequireCurrentUserAsync(cancellationToken);
        var order = await LoadOrderAsync(id, cancellationToken);
        if (order is null) return new(ProductionOrderOperationStatus.NotFound);
        if (!CanSubmitForReview(order, currentUser))
            return new(ProductionOrderOperationStatus.Forbidden, ErrorMessage: "No puedes enviar esta orden a revisión.");
        var users = await FindEligibleCommercialAssistantsAsync(cancellationToken);
        var duplicateNames = users.GroupBy(ReviewerName, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new(ProductionOrderOperationStatus.Success, users.Select(user => new ProductionOrderReviewerDto(
            user.Id, ReviewerName(user), duplicateNames.Contains(ReviewerName(user)) ? user.Email : null)).ToArray());
    }

    public async Task<ProductionOrderOperationResult> SubmitForReviewAsync(Guid id, int version, Guid? reviewerUserId, CancellationToken cancellationToken = default)
    {
        var currentUser = await RequireCurrentUserAsync(cancellationToken);
        var order = await LoadOrderAsync(id, cancellationToken);
        if (order is null) return NotFound();
        if (!CanSubmitForReview(order, currentUser)) return Forbidden("La orden no puede enviarse a revisión con el rol o estado actual.");
        if (version != order.Version) return VersionConflict();
        var validation = ValidateForReview(order);
        if (validation.Count > 0) return Invalid(validation, "Completa la OP y el paquete documental antes de enviarlo a revisión.");

        var reviewers = await FindEligibleCommercialAssistantsAsync(cancellationToken);
        if (reviewers.Length == 0)
            return Conflict("commercial_assistant_configuration", "No hay auxiliares comerciales disponibles para revisar esta orden.");
        // Compatibilidad temporal: el cliente antiguo solo puede omitir el ID si hay una única candidata.
        if (!reviewerUserId.HasValue && reviewers.Length != 1)
            return Conflict("reviewer_required", "Selecciona la Auxiliar Comercial que revisará esta orden.");
        var reviewer = reviewerUserId.HasValue ? reviewers.SingleOrDefault(user => user.Id == reviewerUserId.Value) : reviewers[0];
        if (reviewer is null)
            return Conflict("reviewer_unavailable", "La auxiliar seleccionada ya no está disponible. Consulta la lista y selecciona nuevamente.");
        var previousReviewerUserId = order.ReviewOwnerUserId;

        var now = timeProvider.GetUtcNow();
        order.Status = ProductionOrderStatus.PendingCommercialReview;
        order.ReviewOwnerUserId = reviewer.Id;
        order.CurrentAssigneeUserId = reviewer.Id;
        order.ReviewSubmittedAt = now;
        Touch(order, currentUser.Id, now);
        AddHistory(order, order.Status, currentUser.Id, $"Paquete documental enviado a revisión. Asignada a: {ReviewerName(reviewer)} ({reviewer.Id}). Responsable anterior: {previousReviewerUserId?.ToString() ?? "ninguno"}.", now);
        AddOutbox(order, "commercial-review-requested", reviewer.Id, currentUser.Id, null, now);
        AddAudit(order, currentUser.Id, "commercial.production_order.submitted_for_review", now, new { ReviewerUserId = reviewer.Id, PreviousReviewerUserId = previousReviewerUserId });
        return await SaveAsync(order, currentUser, cancellationToken);
    }

    public async Task<ProductionOrderOperationResult> ReturnForCorrectionAsync(Guid id, int version, string? reason, CancellationToken cancellationToken = default)
    {
        var currentUser = await RequireCurrentUserAsync(cancellationToken);
        var order = await LoadOrderAsync(id, cancellationToken);
        if (order is null) return NotFound();
        if (!CanReview(order, currentUser)) return Forbidden("Solo la Auxiliar Comercial asignada puede devolver esta orden.");
        if (version != order.Version) return VersionConflict();
        var normalizedReason = Normalize(reason);
        if (string.IsNullOrWhiteSpace(normalizedReason) || normalizedReason.Length > 1000)
            return Invalid(new Dictionary<string, string[]> { ["reason"] = ["Indica un motivo obligatorio de máximo 1000 caracteres."] });
        var now = timeProvider.GetUtcNow();
        order.Status = ProductionOrderStatus.CorrectionRequired;
        order.CurrentAssigneeUserId = order.CommercialOwnerUserId;
        order.ReviewReturnedAt = now;
        Touch(order, currentUser.Id, now);
        AddHistory(order, order.Status, currentUser.Id, normalizedReason, now);
        AddOutbox(order, "commercial-correction-required", order.CommercialOwnerUserId, currentUser.Id, normalizedReason, now);
        AddAudit(order, currentUser.Id, "commercial.production_order.returned_for_correction", now);
        return await SaveAsync(order, currentUser, cancellationToken);
    }

    public async Task<ProductionOrderOperationResult> SubmitAsync(
        Guid id,
        int version,
        string? customerOrderNumber,
        CancellationToken cancellationToken = default)
    {
        var currentUser = await RequireCurrentUserAsync(cancellationToken);
        var order = await LoadOrderAsync(id, cancellationToken);
        if (order is null)
        {
            return NotFound();
        }

        if (!CanReview(order, currentUser))
        {
            return Forbidden("Solo la Auxiliar Comercial asignada puede aprobar esta orden.");
        }

        if (version != order.Version)
        {
            return VersionConflict();
        }

        var normalizedCustomerOrderNumber = Normalize(customerOrderNumber);
        if (string.IsNullOrWhiteSpace(normalizedCustomerOrderNumber)
            || normalizedCustomerOrderNumber.Length > 100)
        {
            return Invalid(new Dictionary<string, string[]>
            {
                ["customerOrderNumber"] = ["El número de pedido es obligatorio y debe tener máximo 100 caracteres."],
            });
        }

        var now = timeProvider.GetUtcNow();
        order.CustomerOrderNumber = normalizedCustomerOrderNumber;
        order.Status = ProductionOrderStatus.ReadyForProduction;
        order.SubmittedAt = now;
        order.CurrentAssigneeUserId = null;
        Touch(order, currentUser.Id, now);
        AddHistory(order, order.Status, currentUser.Id, $"Número de pedido {normalizedCustomerOrderNumber} asignado. Paquete aprobado por la Auxiliar Comercial y enviado a Producción.", now);
        AddAudit(order, currentUser.Id, "commercial.production_order.review_approved", now);
        var seller = await dbContext.Users.Where(user => user.Id == order.CommercialOwnerUserId)
            .Select(user => user.FirstName + " " + user.LastName).SingleOrDefaultAsync(cancellationToken) ?? "";
        var registry = dbContext.Set<Portal.Domain.Commercial.Reports.ProductionOrderReportRecord>();
        var historical = await registry.AsNoTracking().ToArrayAsync(cancellationToken);
        var previous = historical.Select(record => (Record: record,
                Data: JsonSerializer.Deserialize<Portal.Application.Commercial.Reports.OpRecordData>(record.DataJson)!))
            .Where(item => !item.Data.SupersededById.HasValue &&
                (item.Data.RegistryId ?? item.Record.ProductionOrderId ?? item.Record.Id) == order.Id)
            .OrderByDescending(item => item.Record.CapturedAt).ThenByDescending(item => item.Data.RegistryVersion)
            .ThenByDescending(item => item.Record.OrderVersion).FirstOrDefault();
        var captured = Portal.Infrastructure.Commercial.Reports.OpRegisterCapture.Create(order, currentUser.Id, seller, now, previous.Data);
        if (previous.Record is not null)
        {
            var capturedData = JsonSerializer.Deserialize<Portal.Application.Commercial.Reports.OpRecordData>(captured.DataJson)!;
            capturedData.PreviousRecordId = previous.Record.Id;
            captured.DataJson = JsonSerializer.Serialize(capturedData);
            var trackedPrevious = registry.Local.FirstOrDefault(record => record.Id == previous.Record.Id);
            if (trackedPrevious is null) dbContext.Attach(previous.Record);
            previous.Data.SupersededById = captured.Id;
            (trackedPrevious ?? previous.Record).DataJson = JsonSerializer.Serialize(previous.Data);
        }
        registry.Add(captured);
        return await SaveAsync(order, currentUser, cancellationToken);
    }

    public async Task<ProductionOrderOperationResult> DuplicateAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var currentUser = await RequireCurrentUserAsync(cancellationToken);
        var source = await LoadOrderAsync(id, cancellationToken);
        if (source is null)
        {
            return NotFound();
        }

        if (!CanView(source, currentUser))
        {
            return Forbidden();
        }

        var now = timeProvider.GetUtcNow();
        var duplicate = new ProductionOrder
        {
            Id = Guid.NewGuid(),
            OperationGroupId = source.OperationGroupId,
            Status = ProductionOrderStatus.Draft,
            CommercialOwnerUserId = currentUser.Id,
            CreatedByUserId = currentUser.Id,
            LastUpdatedByUserId = currentUser.Id,
            CurrentAssigneeUserId = currentUser.Id,
            SourceOrderId = source.Id,
            CustomerOrderNumber = null,
            DeliveryDate = null,
            ClientName = source.ClientName,
            ProductName = source.ProductName,
            ReferenceNumber = source.ReferenceNumber,
            ClientPurchaseOrder = null,
            Quantity = source.Quantity,
            UnitValue = source.UnitValue,
            CityCountry = source.CityCountry,
            Address = source.Address,
            WorkType = CommercialWorkType.Repeat,
            PrintColorProof = source.PrintColorProof,
            DieType = source.DieType,
            OpenSize = source.OpenSize,
            ClosedSize = source.ClosedSize,
            Observations = source.Observations,
            AdditionalSpecifications = source.AdditionalSpecifications,
            ReceptionContact = source.ReceptionContact,
            DeliveryAddress = source.DeliveryAddress,
            ReceptionSchedule = source.ReceptionSchedule,
            PartialDelivery = source.PartialDelivery,
            PartialDeliveryQuantity = source.PartialDeliveryQuantity,
            LegalContractRequirements = source.LegalContractRequirements,
            DispatchDay = null,
            QualityCertificateMode = source.QualityCertificateMode,
            TechnicalSheetMode = source.TechnicalSheetMode,
            CreatedAt = now,
            UpdatedAt = now,
        };

        duplicate.Materials = source.Materials.OrderBy(item => item.Position).Select((item, index) => new ProductionOrderMaterial
        {
            Id = Guid.NewGuid(),
            Position = index,
            Material = item.Material,
            Weight = item.Weight,
            Caliber = item.Caliber,
            OptionalSpecifications = item.OptionalSpecifications,
        }).ToList();
        duplicate.PrintLines = source.PrintLines.OrderBy(item => item.Position).Select((item, index) => new ProductionOrderPrintLine
        {
            Id = Guid.NewGuid(),
            Position = index,
            Product = item.Product,
            Inks = item.Inks,
            Process = item.Process,
            Specials = item.Specials,
        }).ToList();
        duplicate.Finishes = source.Finishes.OrderBy(item => item.Position).Select((item, index) => new ProductionOrderFinish
        {
            Id = Guid.NewGuid(),
            Position = index,
            Specification = item.Specification,
            Front = item.Front,
            Back = item.Back,
            Reserve = item.Reserve,
        }).ToList();
        AddHistory(duplicate, duplicate.Status, currentUser.Id, $"Creada a partir de {FormatCode(source)}.", now);
        dbContext.Set<ProductionOrder>().Add(duplicate);
        AddAudit(duplicate, currentUser.Id, "commercial.production_order.duplicated", now, new { SourceOrderId = source.Id });
        await dbContext.SaveChangesAsync(cancellationToken);
        return Success(await ToDetailAsync(duplicate, currentUser, cancellationToken));
    }

    public async Task<ProductionOrderOperationResult> ReceiveAsync(
        Guid id,
        int version,
        CancellationToken cancellationToken = default)
    {
        var currentUser = await RequireCurrentUserAsync(cancellationToken);
        var order = await LoadOrderAsync(id, cancellationToken);
        if (order is null)
        {
            return NotFound();
        }

        if (!CanEditProduction(order, currentUser) || order.Status != ProductionOrderStatus.ReadyForProduction)
        {
            return Forbidden("La orden no está disponible para recepción en Producción.");
        }

        if (version != order.Version)
        {
            return VersionConflict();
        }

        var now = timeProvider.GetUtcNow();
        order.Status = ProductionOrderStatus.InProduction;
        order.ProductionOwnerUserId = currentUser.Id;
        order.CurrentAssigneeUserId = currentUser.Id;
        order.ProductionReceivedAt = now;
        Touch(order, currentUser.Id, now);
        AddHistory(order, order.Status, currentUser.Id, "Orden recibida por Producción.", now);
        AddAudit(order, currentUser.Id, "commercial.production_order.received", now);
        return await SaveAsync(order, currentUser, cancellationToken);
    }

    public async Task<ProductionOrderOperationResult> UpdateProductionAsync(
        Guid id,
        SaveProductionOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        var currentUser = await RequireCurrentUserAsync(cancellationToken);
        var order = await LoadOrderAsync(id, cancellationToken);
        if (order is null)
        {
            return NotFound();
        }

        if (!CanEditProduction(order, currentUser)
            || order.Status is not (ProductionOrderStatus.ReadyForProduction or ProductionOrderStatus.InProduction))
        {
            return Forbidden("La información productiva no puede modificarse con el rol o estado actual.");
        }

        if (command.Version != order.Version)
        {
            return VersionConflict();
        }

        var validation = ValidateProduction(order, command);
        if (validation.Count > 0)
        {
            return Invalid(validation);
        }

        ApplyProduction(order, command);
        Touch(order, currentUser.Id);
        AddAudit(order, currentUser.Id, "commercial.production_order.production_updated", order.UpdatedAt);
        return await SaveAsync(order, currentUser, cancellationToken);
    }

    public async Task<ProductionOrderOperationResult> CompleteAsync(
        Guid id,
        int version,
        CancellationToken cancellationToken = default)
    {
        var currentUser = await RequireCurrentUserAsync(cancellationToken);
        var order = await LoadOrderAsync(id, cancellationToken);
        if (order is null)
        {
            return NotFound();
        }

        if (order.Status != ProductionOrderStatus.InProduction
            || !(Has(currentUser.Permissions, CommercialPermissionCodes.OrdersComplete)
                || Has(currentUser.Permissions, CommercialPermissionCodes.OrdersManage)))
        {
            return Forbidden("La orden no puede finalizarse con el rol o estado actual.");
        }

        if (version != order.Version)
        {
            return VersionConflict();
        }

        var errors = new Dictionary<string, string[]>();
        if (!order.QualityReviewDate.HasValue) errors["qualityReviewDate"] = ["Registra la fecha de revisión de calidad."];
        if (string.IsNullOrWhiteSpace(order.QualityReviewer)) errors["qualityReviewer"] = ["Registra el responsable de calidad."];
        if (order.QualityApproved != true) errors["qualityApproved"] = ["La revisión de calidad debe estar aprobada para finalizar."];
        if (errors.Count > 0) return Invalid(errors, "Completa y aprueba la revisión de calidad antes de finalizar.");

        var now = timeProvider.GetUtcNow();
        order.Status = ProductionOrderStatus.Completed;
        order.CompletedAt = now;
        Touch(order, currentUser.Id, now);
        AddHistory(order, order.Status, currentUser.Id, "Orden finalizada por Producción.", now);
        AddAudit(order, currentUser.Id, "commercial.production_order.completed", now);
        return await SaveAsync(order, currentUser, cancellationToken);
    }

    public async Task<ProductionOrderOperationResult> CancelAsync(
        Guid id,
        int version,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        var currentUser = await RequireCurrentUserAsync(cancellationToken);
        var order = await LoadOrderAsync(id, cancellationToken);
        if (order is null)
        {
            return NotFound();
        }

        if (!Has(currentUser.Permissions, CommercialPermissionCodes.OrdersManage)
            || order.Status is ProductionOrderStatus.Completed or ProductionOrderStatus.Cancelled)
        {
            return Forbidden("La orden no puede anularse con el rol o estado actual.");
        }

        if (version != order.Version)
        {
            return VersionConflict();
        }

        var normalizedReason = Normalize(reason);
        if (string.IsNullOrWhiteSpace(normalizedReason) || normalizedReason.Length > 500)
        {
            return Invalid(new Dictionary<string, string[]> { ["reason"] = ["Indica un motivo de anulación de máximo 500 caracteres."] });
        }

        var now = timeProvider.GetUtcNow();
        order.Status = ProductionOrderStatus.Cancelled;
        Touch(order, currentUser.Id, now);
        AddHistory(order, order.Status, currentUser.Id, normalizedReason, now);
        AddAudit(order, currentUser.Id, "commercial.production_order.cancelled", now);
        return await SaveAsync(order, currentUser, cancellationToken);
    }

    private async Task<ProductionOrderOperationResult> SaveAsync(
        ProductionOrder order,
        CurrentUserInfo currentUser,
        CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return VersionConflict();
        }
        catch (DbUpdateException exception) when (exception.InnerException is Npgsql.PostgresException
            { SqlState: "23505", ConstraintName: "IX_CommercialOpReportRecords_ProductionOrderId_OrderVersion" })
        {
            return VersionConflict();
        }

        return Success(await ToDetailAsync(order, currentUser, cancellationToken));
    }

    private async Task<ProductionOrder?> LoadOrderAsync(Guid id, CancellationToken cancellationToken) =>
        await dbContext.Set<ProductionOrder>()
            .Include(order => order.Materials)
            .Include(order => order.PrintLines)
            .Include(order => order.Finishes)
            .Include(order => order.StatusHistory)
            .Include(order => order.Documents)
            .Include(order => order.Imports)
            .SingleOrDefaultAsync(order => order.Id == id, cancellationToken);

    private async Task<CurrentUserInfo> RequireCurrentUserAsync(CancellationToken cancellationToken) =>
        await currentUserService.GetCurrentAsync(cancellationToken)
        ?? throw new InvalidOperationException("An authenticated user is required.");

    private static bool CanView(ProductionOrder order, CurrentUserInfo user) =>
        Has(user.Permissions, CommercialPermissionCodes.OrdersManage)
        || order.CommercialOwnerUserId == user.Id
        || (order.CurrentAssigneeUserId == user.Id && Has(user.Permissions, CommercialPermissionCodes.OrdersReview))
        || ((order.Status is ProductionOrderStatus.ReadyForProduction or ProductionOrderStatus.InProduction
            or ProductionOrderStatus.Completed or ProductionOrderStatus.Cancelled)
            && Has(user.Permissions, CommercialPermissionCodes.OrdersViewProduction));

    private static bool CanEditCommercial(ProductionOrder order, CurrentUserInfo user) =>
        order.Status is ProductionOrderStatus.Draft or ProductionOrderStatus.CorrectionRequired
        && (order.CommercialOwnerUserId == user.Id || Has(user.Permissions, CommercialPermissionCodes.OrdersManage))
        && (Has(user.Permissions, CommercialPermissionCodes.OrdersEditCommercial)
            || Has(user.Permissions, CommercialPermissionCodes.OrdersManage));

    private static bool CanSubmitForReview(ProductionOrder order, CurrentUserInfo user) =>
        order.Status is ProductionOrderStatus.Draft or ProductionOrderStatus.CorrectionRequired
        && (order.CommercialOwnerUserId == user.Id || Has(user.Permissions, CommercialPermissionCodes.OrdersManage))
        && (Has(user.Permissions, CommercialPermissionCodes.OrdersSubmitForReview)
            || Has(user.Permissions, CommercialPermissionCodes.OrdersManage));

    private static bool CanReview(ProductionOrder order, CurrentUserInfo user) =>
        order.Status == ProductionOrderStatus.PendingCommercialReview
        && order.CurrentAssigneeUserId == user.Id
        && (Has(user.Permissions, CommercialPermissionCodes.OrdersReview)
            || Has(user.Permissions, CommercialPermissionCodes.OrdersManage));

    private static bool CanEditProduction(ProductionOrder order, CurrentUserInfo user) =>
        Has(user.Permissions, CommercialPermissionCodes.OrdersManage)
        || Has(user.Permissions, CommercialPermissionCodes.OrdersEditProduction);

    private static bool Has(IReadOnlyList<string> permissions, string code) =>
        permissions.Contains(code, StringComparer.Ordinal);

    private static IReadOnlyList<string> AllowedActions(ProductionOrder order, CurrentUserInfo user)
    {
        var actions = new List<string> { "view" };
        if (CanEditCommercial(order, user)) actions.Add("editCommercial");
        if (order.Status == ProductionOrderStatus.Draft && CanEditCommercial(order, user)) actions.Add("discard");
        if (CanSubmitForReview(order, user)) actions.Add("submitForReview");
        if (CanReview(order, user))
        {
            actions.Add("approveReview");
            actions.Add("returnForCorrection");
        }
        if (Has(user.Permissions, CommercialPermissionCodes.OrdersDuplicate)) actions.Add("duplicate");
        if (order.Status == ProductionOrderStatus.ReadyForProduction && CanEditProduction(order, user)) actions.Add("receive");
        if (order.Status is ProductionOrderStatus.ReadyForProduction or ProductionOrderStatus.InProduction && CanEditProduction(order, user)) actions.Add("editProduction");
        if (order.Status == ProductionOrderStatus.InProduction
            && (Has(user.Permissions, CommercialPermissionCodes.OrdersComplete) || Has(user.Permissions, CommercialPermissionCodes.OrdersManage))) actions.Add("complete");
        if (order.Status is not (ProductionOrderStatus.Completed or ProductionOrderStatus.Cancelled)
            && Has(user.Permissions, CommercialPermissionCodes.OrdersManage)) actions.Add("cancel");
        return actions;
    }

    private static Dictionary<string, string[]> ValidateCommercial(SaveCommercialOrderCommand command, bool forSubmission)
    {
        var errors = new Dictionary<string, string[]>();
        CheckText(errors, "customerOrderNumber", command.CustomerOrderNumber, 100);
        CheckText(errors, "quotationNumber", command.QuotationNumber, 100);
        CheckText(errors, "clientName", command.ClientName, 180);
        CheckText(errors, "productName", command.ProductName, 180);
        CheckText(errors, "referenceNumber", command.ReferenceNumber, 100);
        CheckText(errors, "clientPurchaseOrder", command.ClientPurchaseOrder, 100);
        CheckText(errors, "cityCountry", command.CityCountry, 180);
        CheckText(errors, "address", command.Address, 500);
        CheckText(errors, "openSize", command.OpenSize, 100);
        CheckText(errors, "closedSize", command.ClosedSize, 100);
        CheckText(errors, "observations", command.Observations, 2_000);
        CheckText(errors, "additionalSpecifications", command.AdditionalSpecifications, 2_000);
        CheckText(errors, "receptionContact", command.ReceptionContact, 180);
        CheckText(errors, "deliveryAddress", command.DeliveryAddress, 500);
        CheckText(errors, "receptionSchedule", command.ReceptionSchedule, 180);
        CheckText(errors, "legalContractRequirements", command.LegalContractRequirements, 2_000);
        if (command.Quantity < 0) errors["quantity"] = ["La cantidad no puede ser negativa."];
        if (command.UnitValue < 0) errors["unitValue"] = ["El valor unitario no puede ser negativo."];
        if (command.PartialDeliveryQuantity < 0) errors["partialDeliveryQuantity"] = ["La cantidad parcial no puede ser negativa."];
        if (command.PartialDelivery && command.PartialDeliveryQuantity is null or <= 0) errors["partialDeliveryQuantity"] = ["Indica la cantidad de la entrega parcial."];
        if (command.Quantity.HasValue && command.PartialDeliveryQuantity > command.Quantity) errors["partialDeliveryQuantity"] = ["La entrega parcial no puede superar la cantidad de la orden."];
        if ((command.Materials?.Count ?? 0) > MaximumLineCount) errors["materials"] = [$"No puedes registrar más de {MaximumLineCount} materiales."];
        if ((command.PrintLines?.Count ?? 0) > MaximumLineCount) errors["printLines"] = [$"No puedes registrar más de {MaximumLineCount} líneas de impresión."];
        if ((command.Finishes?.Count ?? 0) > MaximumLineCount) errors["finishes"] = [$"No puedes registrar más de {MaximumLineCount} acabados."];

        if (!TryParseEnum<CommercialWorkType>(command.WorkType, out _)) errors["workType"] = ["Selecciona un tipo de trabajo válido."];
        if (!TryParseEnum<DieType>(command.DieType, out _)) errors["dieType"] = ["Selecciona una opción de troquel válida."];
        if (!TryParseEnum<DocumentDeliveryMode>(command.QualityCertificateMode, out _)) errors["qualityCertificateMode"] = ["Selecciona una modalidad válida."];
        if (!TryParseEnum<DocumentDeliveryMode>(command.TechnicalSheetMode, out _)) errors["technicalSheetMode"] = ["Selecciona una modalidad válida."];

        if (forSubmission)
        {
            if (!command.DeliveryDate.HasValue) errors["deliveryDate"] = ["La fecha de entrega es obligatoria."];
            if (string.IsNullOrWhiteSpace(command.ClientName)) errors["clientName"] = ["El cliente es obligatorio."];
            if (string.IsNullOrWhiteSpace(command.ProductName)) errors["productName"] = ["El producto es obligatorio."];
            if (command.Quantity is null or <= 0) errors["quantity"] = ["La cantidad debe ser mayor que cero."];
            if (!TryParseEnum<CommercialWorkType>(command.WorkType, out var workType) || workType == CommercialWorkType.Unspecified) errors["workType"] = ["Selecciona el tipo de trabajo."];
        }

        return errors;
    }

    private static Dictionary<string, string[]> ValidateForReview(ProductionOrder order)
    {
        var errors = ValidateCommercial(new SaveCommercialOrderCommand(
            order.Version, order.CustomerOrderNumber, order.QuotationNumber, order.DeliveryDate, order.ClientName, order.ProductName,
            order.ReferenceNumber, order.ClientPurchaseOrder, order.Quantity, order.UnitValue, order.CityCountry,
            order.Address, order.WorkType.ToString(), order.PrintColorProof, order.DieType.ToString(), order.OpenSize,
            order.ClosedSize, order.Observations, order.AdditionalSpecifications, order.ReceptionContact,
            order.DeliveryAddress, order.ReceptionSchedule, order.PartialDelivery, order.PartialDeliveryQuantity,
            order.LegalContractRequirements, order.DispatchDay, order.QualityCertificateMode.ToString(),
            order.TechnicalSheetMode.ToString(), [], [], []), true);
        if (!order.Documents.Any(document => document.IsActive && document.Type == ProductionOrderDocumentType.Quotation))
            errors["quotation"] = ["La cotización es obligatoria."];
        if (order.PurchaseOrderApplicability == DocumentApplicabilityStatus.Pending)
            errors["purchaseOrder"] = ["Adjunta la orden de compra o marca No aplica."];
        if (order.DesignApplicability == DocumentApplicabilityStatus.Pending)
            errors["design"] = ["Adjunta el documento de diseño o marca No requiere Diseño."];
        return errors;
    }

    private static Dictionary<string, string[]> ValidateProduction(ProductionOrder order, SaveProductionOrderCommand command)
    {
        var errors = new Dictionary<string, string[]>();
        CheckText(errors, "planningManager", command.PlanningManager, 180);
        CheckText(errors, "cuttingManager", command.CuttingManager, 180);
        CheckText(errors, "printingManagerShift1", command.PrintingManagerShift1, 180);
        CheckText(errors, "printingManagerShift2", command.PrintingManagerShift2, 180);
        CheckText(errors, "finishingManager", command.FinishingManager, 180);
        CheckText(errors, "dieCutManager", command.DieCutManager, 180);
        CheckText(errors, "dieMachine", command.DieMachine, 120);
        CheckText(errors, "dieNumber", command.DieNumber, 120);
        CheckText(errors, "gluingManager", command.GluingManager, 180);
        CheckText(errors, "glueType", command.GlueType, 120);
        CheckText(errors, "qualityReviewer", command.QualityReviewer, 180);
        CheckText(errors, "qualityNotes", command.QualityNotes, 2_000);
        CheckNonNegative(errors, "dieTotalProcessed", command.DieTotalProcessed);
        CheckNonNegative(errors, "dieConforming", command.DieConforming);
        CheckNonNegative(errors, "dieNonConforming", command.DieNonConforming);
        CheckNonNegative(errors, "glueTotalProcessed", command.GlueTotalProcessed);
        CheckNonNegative(errors, "glueConforming", command.GlueConforming);
        CheckNonNegative(errors, "glueNonConforming", command.GlueNonConforming);
        ValidateChildIds(errors, "materials", order.Materials.Select(item => item.Id), command.Materials?.Select(item => item.Id));
        ValidateChildIds(errors, "printLines", order.PrintLines.Select(item => item.Id), command.PrintLines?.Select(item => item.Id));
        ValidateChildIds(errors, "finishes", order.Finishes.Select(item => item.Id), command.Finishes?.Select(item => item.Id));
        return errors;
    }

    private static void ApplyCommercial(ProductionOrder order, SaveCommercialOrderCommand command)
    {
        // El número de pedido pertenece a la Auxiliar Comercial y se asigna
        // únicamente al aprobar la revisión; los cambios del agente no lo alteran.
        order.QuotationNumber = Normalize(command.QuotationNumber);
        order.DeliveryDate = command.DeliveryDate;
        order.ClientName = Normalize(command.ClientName);
        order.ProductName = Normalize(command.ProductName);
        order.ReferenceNumber = Normalize(command.ReferenceNumber);
        order.ClientPurchaseOrder = Normalize(command.ClientPurchaseOrder);
        order.Quantity = command.Quantity;
        order.UnitValue = command.UnitValue;
        order.CityCountry = Normalize(command.CityCountry);
        order.Address = Normalize(command.Address);
        order.WorkType = ParseEnum(command.WorkType, CommercialWorkType.Unspecified);
        order.PrintColorProof = command.PrintColorProof;
        order.DieType = ParseEnum(command.DieType, DieType.None);
        order.OpenSize = Normalize(command.OpenSize);
        order.ClosedSize = Normalize(command.ClosedSize);
        order.Observations = Normalize(command.Observations);
        order.AdditionalSpecifications = Normalize(command.AdditionalSpecifications);
        order.ReceptionContact = Normalize(command.ReceptionContact);
        order.DeliveryAddress = Normalize(command.DeliveryAddress);
        order.ReceptionSchedule = Normalize(command.ReceptionSchedule);
        order.PartialDelivery = command.PartialDelivery;
        order.PartialDeliveryQuantity = command.PartialDelivery ? command.PartialDeliveryQuantity : null;
        order.LegalContractRequirements = Normalize(command.LegalContractRequirements);
        order.DispatchDay = command.DispatchDay;
        order.QualityCertificateMode = ParseEnum(command.QualityCertificateMode, DocumentDeliveryMode.None);
        order.TechnicalSheetMode = ParseEnum(command.TechnicalSheetMode, DocumentDeliveryMode.None);
    }

    private void ReplaceCommercialLines(ProductionOrder order, SaveCommercialOrderCommand command)
    {
        order.Materials.Clear();
        foreach (var material in (command.Materials ?? []).Where(HasMaterialContent).Take(MaximumLineCount).Select((item, index) => new ProductionOrderMaterial
        {
            Id = Guid.NewGuid(), ProductionOrderId = order.Id, ProductionOrder = order,
            Position = index, Material = Normalize(item.Material), Weight = Normalize(item.Weight),
            Caliber = Normalize(item.Caliber), OptionalSpecifications = Normalize(item.OptionalSpecifications),
        }))
        {
            order.Materials.Add(material);
            dbContext.Set<ProductionOrderMaterial>().Add(material);
        }

        order.PrintLines.Clear();
        foreach (var printLine in (command.PrintLines ?? []).Where(HasPrintContent).Take(MaximumLineCount).Select((item, index) => new ProductionOrderPrintLine
        {
            Id = Guid.NewGuid(), ProductionOrderId = order.Id, ProductionOrder = order,
            Position = index, Product = Normalize(item.Product), Inks = Normalize(item.Inks),
            Process = Normalize(item.Process), Specials = Normalize(item.Specials),
        }))
        {
            order.PrintLines.Add(printLine);
            dbContext.Set<ProductionOrderPrintLine>().Add(printLine);
        }

        order.Finishes.Clear();
        foreach (var finish in (command.Finishes ?? []).Where(HasFinishContent).Take(MaximumLineCount).Select((item, index) => new ProductionOrderFinish
        {
            Id = Guid.NewGuid(), ProductionOrderId = order.Id, ProductionOrder = order,
            Position = index, Specification = Normalize(item.Specification), Front = item.Front, Back = item.Back, Reserve = item.Reserve,
        }))
        {
            order.Finishes.Add(finish);
            dbContext.Set<ProductionOrderFinish>().Add(finish);
        }
    }

    private static string ReviewerName(ApplicationUser user) => $"{user.FirstName} {user.LastName}".Trim();

    private async Task<ApplicationUser[]> FindEligibleCommercialAssistantsAsync(CancellationToken cancellationToken)
    {
        string[] requiredPermissions = [CommercialPermissionCodes.OrdersView, CommercialPermissionCodes.OrdersReview, CommercialPermissionCodes.OrdersSubmit];
        var roleId = await dbContext.Roles.AsNoTracking()
            .Where(role => role.NormalizedName == "AUXILIAR COMERCIAL" && role.IsActive)
            .Select(role => (Guid?)role.Id).SingleOrDefaultAsync(cancellationToken);
        if (!roleId.HasValue) return [];
        var granted = await (from rolePermission in dbContext.RolePermissions.AsNoTracking()
            join permission in dbContext.Permissions.AsNoTracking() on rolePermission.PermissionId equals permission.Id
            where rolePermission.RoleId == roleId.Value && permission.IsActive && requiredPermissions.Contains(permission.Code)
            select permission.Code).Distinct().ToArrayAsync(cancellationToken);
        if (requiredPermissions.Any(code => !granted.Contains(code))) return [];
        var users = await (from userRole in dbContext.Set<ApplicationUserRole>().AsNoTracking()
            join user in dbContext.Users.AsNoTracking() on userRole.UserId equals user.Id
            where userRole.RoleId == roleId.Value && user.IsActive && user.Email != null && user.Email != ""
            select user).Distinct().ToArrayAsync(cancellationToken);
        return users.Where(user => MailAddress.TryCreate(user.Email, out var email)
                && string.Equals(email.Address, user.Email, StringComparison.OrdinalIgnoreCase))
            .OrderBy(ReviewerName, StringComparer.OrdinalIgnoreCase).ThenBy(user => user.Id).ToArray();
    }

    private void AddOutbox(
        ProductionOrder order,
        string notificationType,
        Guid recipientUserId,
        Guid actorUserId,
        string? reason,
        DateTimeOffset now)
    {
        dbContext.Set<CommercialNotificationOutbox>().Add(new CommercialNotificationOutbox
        {
            Id = Guid.NewGuid(),
            ProductionOrderId = order.Id,
            NotificationType = notificationType,
            RecipientUserId = recipientUserId,
            PayloadJson = JsonSerializer.Serialize(new {
                ActorUserId = actorUserId, Reason = reason, OrderVersion = order.Version,
                ReviewSubmittedAt = order.ReviewSubmittedAt, ReviewReturnedAt = order.ReviewReturnedAt,
            }),
            Status = CommercialOutboxStatus.Pending,
            NextAttemptAt = now,
            CreatedAt = now,
        });
    }

    private static async Task<BufferedFile> BufferAsync(UploadedFileCommand file, CancellationToken cancellationToken)
    {
        await using var memory = new MemoryStream();
        await file.Content.CopyToAsync(memory, cancellationToken);
        return new BufferedFile(file.FileName, file.ContentType, memory.ToArray());
    }

    private static bool TrySelect(
        QuotationPreviewDto preview,
        ImportQuotationSelection selection,
        out QuotationItemDto item,
        out QuotationPriceOptionDto option)
    {
        item = null!;
        option = null!;
        if (selection.ItemIndex < 0 || selection.ItemIndex >= preview.Items.Count) return false;
        item = preview.Items[selection.ItemIndex];
        if (selection.OptionIndex < 0 || selection.OptionIndex >= item.Options.Count) return false;
        option = item.Options[selection.OptionIndex];
        return true;
    }

    private static ProductionOrderDocument CreateDocument(
        ProductionOrder order,
        ProductionOrderDocumentType type,
        StoredDocument stored,
        Guid userId,
        DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        ProductionOrderId = order.Id,
        Type = type,
        Applicability = DocumentApplicabilityStatus.Attached,
        OriginalFileName = stored.OriginalFileName,
        StorageKey = stored.StorageKey,
        ContentType = stored.ContentType,
        Size = stored.Size,
        Sha256 = stored.Sha256,
        IsActive = true,
        UploadedByUserId = userId,
        UploadedAt = now,
    };

    private static ProductionOrderImport CreateImport(
        ProductionOrder order,
        ProductionOrderDocument document,
        QuotationPreviewDto preview,
        ImportQuotationSelection selection,
        Guid userId,
        DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        ProductionOrderId = order.Id,
        SourceDocumentId = document.Id,
        TemplateFingerprint = preview.TemplateFingerprint,
        ImporterVersion = "1.0.0",
        ExtractedDataJson = JsonSerializer.Serialize(new { Preview = preview, Selection = selection }),
        WarningsJson = JsonSerializer.Serialize(preview.Warnings),
        ImportedByUserId = userId,
        ImportedAt = now,
    };

    private static void ApplyImportedSelection(
        ProductionOrder order,
        QuotationPreviewDto preview,
        QuotationItemDto item,
        QuotationPriceOptionDto option)
    {
        order.QuotationNumber = Normalize(preview.QuotationNumber);
        order.ClientName = Normalize(preview.ClientName);
        order.ProductName = Normalize(item.ProductName);
        order.ReferenceNumber = ExtractReference(item.Description);
        order.Quantity = option.Quantity;
        order.UnitValue = option.UnitValue;
        order.CityCountry = Normalize(preview.CityCountry);
        order.Address = Normalize(preview.Address);
        order.OpenSize = Normalize(item.OpenSize);
        order.Observations = Normalize(item.Description);
        var deliveryMatch = System.Text.RegularExpressions.Regex.Match(
            item.Description,
            @"NOTA\s*:?\s*(\d+)\s+ENTREGAS",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (deliveryMatch.Success
            && int.TryParse(deliveryMatch.Groups[1].Value, out var deliveryCount)
            && deliveryCount > 1)
        {
            order.PartialDelivery = true;
            order.PartialDeliveryQuantity = option.Quantity / deliveryCount;
        }
    }

    private static string? ExtractReference(string description)
    {
        var match = System.Text.RegularExpressions.Regex.Match(description, @"(?:ITEM|REF(?:ERENCIA)?)\s*[:#-]?\s*([A-Z0-9-]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static void AddImportedLines(ProductionOrder order, QuotationItemDto item)
    {
        if (!string.IsNullOrWhiteSpace(item.Material) || !string.IsNullOrWhiteSpace(item.Caliber))
            order.Materials.Add(new ProductionOrderMaterial
            {
                Id = Guid.NewGuid(), ProductionOrderId = order.Id, ProductionOrder = order, Position = 0,
                Material = Normalize(item.Material), Weight = Normalize(item.Caliber),
            });
        if (!string.IsNullOrWhiteSpace(item.Inks) || !string.IsNullOrWhiteSpace(item.Process))
            order.PrintLines.Add(new ProductionOrderPrintLine
            {
                Id = Guid.NewGuid(), ProductionOrderId = order.Id, ProductionOrder = order, Position = 0,
                Product = Normalize(item.ProductName),
                Inks = Normalize(item.Inks)?.Replace("X", " X ", StringComparison.OrdinalIgnoreCase),
                Process = Normalize(item.Process),
            });
    }

    private void ReplaceImportedLines(ProductionOrder order, QuotationItemDto item)
    {
        order.Materials.Clear();
        order.PrintLines.Clear();
        AddImportedLines(order, item);
    }

    private static bool TryDocumentType(string value, out ProductionOrderDocumentType type) =>
        Enum.TryParse(value, true, out type);

    private sealed record BufferedFile(string FileName, string ContentType, byte[] Bytes)
    {
        public UploadedFileCommand Command() => new(FileName, ContentType, Bytes.LongLength, new MemoryStream(Bytes, writable: false));
    }

    private static void ApplyProduction(ProductionOrder order, SaveProductionOrderCommand command)
    {
        order.PlanningDate = command.PlanningDate;
        order.PlanningManager = Normalize(command.PlanningManager);
        order.MaterialCutDate = command.MaterialCutDate;
        order.CuttingManager = Normalize(command.CuttingManager);
        order.PrintStartShift1 = command.PrintStartShift1;
        order.PrintingManagerShift1 = Normalize(command.PrintingManagerShift1);
        order.PrintStartShift2 = command.PrintStartShift2;
        order.PrintingManagerShift2 = Normalize(command.PrintingManagerShift2);
        order.FinishingStart = command.FinishingStart;
        order.FinishingManager = Normalize(command.FinishingManager);
        order.DieCutStart = command.DieCutStart;
        order.DieCutManager = Normalize(command.DieCutManager);
        order.DieMachine = Normalize(command.DieMachine);
        order.DieNumber = Normalize(command.DieNumber);
        order.DieTotalProcessed = command.DieTotalProcessed;
        order.DieConforming = command.DieConforming;
        order.DieNonConforming = command.DieNonConforming;
        order.GluingStart = command.GluingStart;
        order.GluingManager = Normalize(command.GluingManager);
        order.GlueType = Normalize(command.GlueType);
        order.GlueTotalProcessed = command.GlueTotalProcessed;
        order.GlueConforming = command.GlueConforming;
        order.GlueNonConforming = command.GlueNonConforming;
        order.QualityReviewDate = command.QualityReviewDate;
        order.QualityReviewer = Normalize(command.QualityReviewer);
        order.QualityApproved = command.QualityApproved;
        order.QualityNotes = Normalize(command.QualityNotes);

        foreach (var input in command.Materials ?? [])
        {
            var item = order.Materials.Single(candidate => candidate.Id == input.Id);
            item.SheetSize = Normalize(input.SheetSize);
            item.SheetQuantity = input.SheetQuantity;
            item.CutSize = Normalize(input.CutSize);
            item.FractionPerSheet = input.FractionPerSheet;
            item.FitPerFraction = input.FitPerFraction;
            item.TotalCutQuantity = input.TotalCutQuantity;
            item.ConformingQuantity = input.ConformingQuantity;
            item.NonConformingQuantity = input.NonConformingQuantity;
        }

        foreach (var input in command.PrintLines ?? [])
        {
            var item = order.PrintLines.Single(candidate => candidate.Id == input.Id);
            item.Machine = Normalize(input.Machine);
            item.Mounting = Normalize(input.Mounting);
            item.ShotsToProcess = input.ShotsToProcess;
            item.ConformingQuantity = input.ConformingQuantity;
            item.NonConformingQuantity = input.NonConformingQuantity;
        }

        foreach (var input in command.Finishes ?? [])
        {
            var item = order.Finishes.Single(candidate => candidate.Id == input.Id);
            item.TotalProcessed = input.TotalProcessed;
            item.ConformingQuantity = input.ConformingQuantity;
            item.NonConformingQuantity = input.NonConformingQuantity;
        }
    }

    private void Touch(ProductionOrder order, Guid userId, DateTimeOffset? now = null)
    {
        order.LastUpdatedByUserId = userId;
        order.UpdatedAt = now ?? timeProvider.GetUtcNow();
        order.Version++;
    }

    private void AddHistory(ProductionOrder order, ProductionOrderStatus status, Guid actorId, string? note, DateTimeOffset now)
    {
        var history = new ProductionOrderStatusHistory
        {
            Id = Guid.NewGuid(), Status = status, ActorUserId = actorId, Note = note, OccurredAt = now,
        };
        order.StatusHistory.Add(history);
        if (dbContext.Entry(order).State != EntityState.Detached)
        {
            dbContext.Set<ProductionOrderStatusHistory>().Add(history);
        }
    }

    private void AddAudit(ProductionOrder order, Guid actorId, string action, DateTimeOffset now, object? extra = null)
    {
        var metadata = new Dictionary<string, object?>
        {
            ["OrderCode"] = FormatCode(order),
            ["Status"] = StatusValue(order.Status),
        };
        if (extra is not null)
        {
            foreach (var property in extra.GetType().GetProperties()) metadata[property.Name] = property.GetValue(extra);
        }

        dbContext.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid(), ActorUserId = actorId, Action = action, EntityType = "CommercialProductionOrder",
            EntityId = order.Id.ToString(), Result = "Success", OccurredAt = now, Metadata = JsonSerializer.Serialize(metadata),
        });
    }

    private async Task<ProductionOrderDetailDto> ToDetailAsync(ProductionOrder order, CurrentUserInfo user, CancellationToken cancellationToken)
    {
        var relatedOrders = await dbContext.Set<ProductionOrder>().AsNoTracking()
            .Where(candidate => candidate.OperationGroupId == order.OperationGroupId && candidate.Id != order.Id)
            .OrderBy(candidate => candidate.Consecutive)
            .ToListAsync(cancellationToken);
        var people = await LoadPeopleAsync(
            order.StatusHistory.Select(item => (Guid?)item.ActorUserId)
                .Concat(new[] { (Guid?)order.CommercialOwnerUserId, order.CurrentAssigneeUserId, order.ReviewOwnerUserId, order.ProductionOwnerUserId }),
            cancellationToken);
        var commercialOwner = people[order.CommercialOwnerUserId];
        var currentAssignee = order.CurrentAssigneeUserId is { } currentAssigneeId ? people.GetValueOrDefault(currentAssigneeId) : null;
        var reviewOwner = order.ReviewOwnerUserId is { } reviewOwnerId ? people.GetValueOrDefault(reviewOwnerId) : null;
        var productionOwner = order.ProductionOwnerUserId is { } productionOwnerId ? people.GetValueOrDefault(productionOwnerId) : null;
        var activeDocuments = order.Documents.Where(document => document.IsActive).OrderBy(document => document.Type).ThenBy(document => document.UploadedAt).ToArray();
        var quotationReady = activeDocuments.Any(document => document.Type == ProductionOrderDocumentType.Quotation);
        var checklistComplete = quotationReady
            && order.PurchaseOrderApplicability != DocumentApplicabilityStatus.Pending
            && order.DesignApplicability != DocumentApplicabilityStatus.Pending;
        return new ProductionOrderDetailDto(
            order.Id, FormatCode(order), StatusValue(order.Status), order.Version, order.SourceOrderId, order.OperationGroupId,
            commercialOwner, currentAssignee, reviewOwner, productionOwner,
            new CommercialOrderDto(
                order.CustomerOrderNumber, order.QuotationNumber, order.DeliveryDate, order.ClientName, order.ProductName, order.ReferenceNumber,
                order.ClientPurchaseOrder, order.Quantity, order.UnitValue, order.Quantity * order.UnitValue,
                order.CityCountry, order.Address, EnumValue(order.WorkType), order.PrintColorProof, EnumValue(order.DieType),
                order.OpenSize, order.ClosedSize, order.Observations, order.AdditionalSpecifications,
                order.ReceptionContact, order.DeliveryAddress, order.ReceptionSchedule, order.PartialDelivery,
                order.PartialDeliveryQuantity, order.LegalContractRequirements, order.DispatchDay,
                EnumValue(order.QualityCertificateMode), EnumValue(order.TechnicalSheetMode)),
            new ProductionOrderDataDto(
                order.PlanningDate, order.PlanningManager, order.MaterialCutDate, order.CuttingManager,
                order.PrintStartShift1, order.PrintingManagerShift1, order.PrintStartShift2, order.PrintingManagerShift2,
                order.FinishingStart, order.FinishingManager, order.DieCutStart, order.DieCutManager, order.DieMachine,
                order.DieNumber, order.DieTotalProcessed, order.DieConforming, order.DieNonConforming,
                order.GluingStart, order.GluingManager, order.GlueType, order.GlueTotalProcessed,
                order.GlueConforming, order.GlueNonConforming, order.QualityReviewDate, order.QualityReviewer,
                order.QualityApproved, order.QualityNotes),
            order.Materials.OrderBy(item => item.Position).Select(item => new ProductionOrderMaterialDto(
                item.Id, item.Position, item.Material, item.Weight, item.Caliber, item.OptionalSpecifications,
                item.SheetSize, item.SheetQuantity, item.CutSize, item.FractionPerSheet, item.FitPerFraction,
                item.TotalCutQuantity, item.ConformingQuantity, item.NonConformingQuantity)).ToArray(),
            order.PrintLines.OrderBy(item => item.Position).Select(item => new ProductionOrderPrintLineDto(
                item.Id, item.Position, item.Product, item.Inks, item.Process, item.Specials, item.Machine,
                item.Mounting, item.ShotsToProcess, item.ConformingQuantity, item.NonConformingQuantity)).ToArray(),
            order.Finishes.OrderBy(item => item.Position).Select(item => new ProductionOrderFinishDto(
                item.Id, item.Position, item.Specification, item.Front, item.Back, item.Reserve,
                item.TotalProcessed, item.ConformingQuantity, item.NonConformingQuantity)).ToArray(),
            order.StatusHistory.OrderByDescending(item => item.OccurredAt).Select(item => new ProductionOrderHistoryDto(
                item.Id, StatusValue(item.Status), people[item.ActorUserId], item.Note, item.OccurredAt)).ToArray(),
            activeDocuments.Select(document => new ProductionOrderDocumentDto(
                document.Id, EnumValue(document.Type), EnumValue(document.Applicability), document.OriginalFileName,
                document.ContentType, document.Size, document.Sha256, document.UploadedAt)).ToArray(),
            new ProductionOrderChecklistDto(quotationReady, EnumValue(order.PurchaseOrderApplicability), EnumValue(order.DesignApplicability), checklistComplete),
            relatedOrders.Where(candidate => CanView(candidate, user)).Select(candidate => new RelatedProductionOrderDto(
                candidate.Id, FormatCode(candidate), StatusValue(candidate.Status), candidate.ProductName, candidate.Quantity)).ToArray(),
            order.StatusHistory.Where(item => item.Status == ProductionOrderStatus.CorrectionRequired)
                .OrderByDescending(item => item.OccurredAt).Select(item => item.Note).FirstOrDefault(),
            order.CreatedAt, order.UpdatedAt, order.SubmittedAt, order.ReviewSubmittedAt, order.ReviewReturnedAt, order.ProductionReceivedAt, order.CompletedAt,
            AllowedActions(order, user));
    }

    private async Task<Dictionary<Guid, ProductionOrderPersonDto>> LoadPeopleAsync(IEnumerable<Guid?> ids, CancellationToken cancellationToken)
    {
        var uniqueIds = ids.Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToArray();
        return await dbContext.Users.AsNoTracking()
            .Where(user => uniqueIds.Contains(user.Id))
            .Select(user => new ProductionOrderPersonDto(user.Id, (user.FirstName + " " + user.LastName).Trim()))
            .ToDictionaryAsync(person => person.Id, cancellationToken);
    }

    private static ProductionOrderSummaryDto ToSummary(ProductionOrder order, ProductionOrderPersonDto owner, ProductionOrderPersonDto? productionOwner, IReadOnlyList<string> actions) =>
        new(order.Id, FormatCode(order), StatusValue(order.Status), order.Version, order.CustomerOrderNumber, order.QuotationNumber, order.ClientName,
            order.ProductName, order.DeliveryDate, order.Quantity, order.Quantity * order.UnitValue, owner, productionOwner,
            order.CreatedAt, order.UpdatedAt, actions);

    private static string FormatCode(ProductionOrder order) => $"OP-{order.CreatedAt.Year}-{order.Consecutive:00000}";
    private static string StatusValue(ProductionOrderStatus status) => status switch
    {
        ProductionOrderStatus.Draft => "draft",
        ProductionOrderStatus.PendingCommercialReview => "pendingCommercialReview",
        ProductionOrderStatus.CorrectionRequired => "correctionRequired",
        ProductionOrderStatus.ReadyForProduction => "readyForProduction",
        ProductionOrderStatus.InProduction => "inProduction",
        ProductionOrderStatus.Completed => "completed",
        ProductionOrderStatus.Cancelled => "cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };
    private static string EnumValue<T>(T value) where T : struct, Enum => char.ToLowerInvariant(value.ToString()[0]) + value.ToString()[1..];
    private static bool TryParseStatus(string value, out ProductionOrderStatus status) =>
        Enum.TryParse(value.Replace("readyForProduction", "ReadyForProduction", StringComparison.OrdinalIgnoreCase).Replace("inProduction", "InProduction", StringComparison.OrdinalIgnoreCase), true, out status);
    private static bool TryParseEnum<T>(string? value, out T result) where T : struct, Enum => Enum.TryParse(value, true, out result);
    private static T ParseEnum<T>(string? value, T fallback) where T : struct, Enum => TryParseEnum<T>(value, out var result) ? result : fallback;
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static bool HasMaterialContent(CommercialMaterialInput item) => new[] { item.Material, item.Weight, item.Caliber, item.OptionalSpecifications }.Any(value => !string.IsNullOrWhiteSpace(value));
    private static bool HasPrintContent(CommercialPrintLineInput item) => new[] { item.Product, item.Inks, item.Process, item.Specials }.Any(value => !string.IsNullOrWhiteSpace(value));
    private static bool HasFinishContent(CommercialFinishInput item) => !string.IsNullOrWhiteSpace(item.Specification) || item.Front || item.Back || item.Reserve;

    private static void CheckText(IDictionary<string, string[]> errors, string key, string? value, int maximumLength)
    {
        if (value?.Trim().Length > maximumLength) errors[key] = [$"El campo no puede superar {maximumLength} caracteres."];
    }

    private static void CheckNonNegative(IDictionary<string, string[]> errors, string key, decimal? value)
    {
        if (value < 0) errors[key] = ["El valor no puede ser negativo."];
    }

    private static void ValidateChildIds(IDictionary<string, string[]> errors, string key, IEnumerable<Guid> validIds, IEnumerable<Guid>? providedIds)
    {
        var valid = validIds.ToHashSet();
        if ((providedIds ?? []).Any(id => !valid.Contains(id))) errors[key] = ["La solicitud contiene filas que no pertenecen a esta orden."];
    }

    private static ProductionOrderOperationResult Success(ProductionOrderDetailDto order) => new(ProductionOrderOperationStatus.Success, order);
    private static ProductionOrderOperationResult NotFound() => new(ProductionOrderOperationStatus.NotFound, ErrorCode: "production_order_not_found", ErrorMessage: "La orden de producción no existe.");
    private static ProductionOrderOperationResult Forbidden(string message = "No tienes acceso a esta orden de producción.") => new(ProductionOrderOperationStatus.Forbidden, ErrorCode: "production_order_forbidden", ErrorMessage: message);
    private static ProductionOrderOperationResult Invalid(IReadOnlyDictionary<string, string[]> errors, string message = "Revisa la información ingresada.") => new(ProductionOrderOperationStatus.Invalid, ErrorCode: "production_order_validation", ErrorMessage: message, ValidationErrors: errors);
    private static ProductionOrderOperationResult Conflict(string code, string message) => new(ProductionOrderOperationStatus.Conflict, ErrorCode: code, ErrorMessage: message);
    private static ProductionOrderOperationResult VersionConflict() => new(ProductionOrderOperationStatus.Conflict, ErrorCode: "production_order_version_conflict", ErrorMessage: "La orden cambió mientras la estabas editando. Actualiza la página antes de continuar.");
}
