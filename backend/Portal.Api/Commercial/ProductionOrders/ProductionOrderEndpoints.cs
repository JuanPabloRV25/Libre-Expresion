using System.Text.Json;
using Portal.Api.Authentication;
using Portal.Application.Commercial.ProductionOrders;
using Portal.Domain.Commercial.ProductionOrders;

namespace Portal.Api.Commercial.ProductionOrders;

public static class ProductionOrderEndpoints
{
    private static readonly JsonSerializerOptions FormJsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapCommercialProductionOrderEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/commercial/production-orders");

        group.MapGet("/", ListAsync)
            .RequireAuthorization(CommercialPermissionCodes.OrdersView);
        group.MapGet("/{id:guid}", GetAsync)
            .RequireAuthorization(CommercialPermissionCodes.OrdersView);
        group.MapPost("/", CreateAsync)
            .RequireAuthorization(CommercialPermissionCodes.OrdersCreate)
            .AddEndpointFilter<AntiforgeryEndpointFilter>();
        group.MapPut("/{id:guid}/commercial", UpdateCommercialAsync)
            .RequireAuthorization(CommercialPermissionCodes.OrdersEditCommercial)
            .AddEndpointFilter<AntiforgeryEndpointFilter>();
        group.MapPost("/quotation-preview", PreviewQuotationAsync)
            .RequireAuthorization(CommercialPermissionCodes.OrdersCreate)
            .AddEndpointFilter<AntiforgeryEndpointFilter>();
        group.MapPost("/import-quotation", ImportQuotationAsync)
            .RequireAuthorization(CommercialPermissionCodes.OrdersCreate)
            .AddEndpointFilter<AntiforgeryEndpointFilter>();
        group.MapPut("/{id:guid}/quotation", ReplaceQuotationAsync)
            .RequireAuthorization(CommercialPermissionCodes.OrdersEditCommercial)
            .AddEndpointFilter<AntiforgeryEndpointFilter>();
        group.MapPost("/{id:guid}/documents/{type}", AddDocumentAsync)
            .RequireAuthorization(CommercialPermissionCodes.OrdersEditCommercial)
            .AddEndpointFilter<AntiforgeryEndpointFilter>();
        group.MapPut("/{id:guid}/documents/{type}/applicability", SetDocumentApplicabilityAsync)
            .RequireAuthorization(CommercialPermissionCodes.OrdersEditCommercial)
            .AddEndpointFilter<AntiforgeryEndpointFilter>();
        group.MapGet("/{id:guid}/documents/{documentId:guid}", DownloadDocumentAsync)
            .RequireAuthorization(CommercialPermissionCodes.OrdersView);
        group.MapDelete("/{id:guid}/documents/{documentId:guid}", DeleteDocumentAsync)
            .RequireAuthorization(CommercialPermissionCodes.OrdersEditCommercial)
            .AddEndpointFilter<AntiforgeryEndpointFilter>();
        group.MapDelete("/{id:guid}/draft", DiscardDraftAsync)
            .RequireAuthorization(CommercialPermissionCodes.OrdersEditCommercial)
            .AddEndpointFilter<AntiforgeryEndpointFilter>();
        group.MapGet("/{id:guid}/reviewers", GetReviewersAsync)
            .RequireAuthorization(CommercialPermissionCodes.OrdersSubmitForReview);
        group.MapPost("/{id:guid}/submit-for-review", SubmitForReviewAsync)
            .RequireAuthorization(CommercialPermissionCodes.OrdersSubmitForReview)
            .AddEndpointFilter<AntiforgeryEndpointFilter>();
        group.MapPost("/{id:guid}/return-for-correction", ReturnForCorrectionAsync)
            .RequireAuthorization(CommercialPermissionCodes.OrdersReview)
            .AddEndpointFilter<AntiforgeryEndpointFilter>();
        group.MapPost("/{id:guid}/submit", SubmitAsync)
            .RequireAuthorization(CommercialPermissionCodes.OrdersSubmit)
            .AddEndpointFilter<AntiforgeryEndpointFilter>();
        group.MapPost("/{id:guid}/duplicate", DuplicateAsync)
            .RequireAuthorization(CommercialPermissionCodes.OrdersDuplicate)
            .AddEndpointFilter<AntiforgeryEndpointFilter>();
        group.MapPost("/{id:guid}/receive", ReceiveAsync)
            .RequireAuthorization(CommercialPermissionCodes.OrdersEditProduction)
            .AddEndpointFilter<AntiforgeryEndpointFilter>();
        group.MapPut("/{id:guid}/production", UpdateProductionAsync)
            .RequireAuthorization(CommercialPermissionCodes.OrdersEditProduction)
            .AddEndpointFilter<AntiforgeryEndpointFilter>();
        group.MapPost("/{id:guid}/complete", CompleteAsync)
            .RequireAuthorization(CommercialPermissionCodes.OrdersComplete)
            .AddEndpointFilter<AntiforgeryEndpointFilter>();
        group.MapPost("/{id:guid}/cancel", CancelAsync)
            .RequireAuthorization(CommercialPermissionCodes.OrdersManage)
            .AddEndpointFilter<AntiforgeryEndpointFilter>();

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        string? search,
        string? status,
        bool? assignedToMe,
        IProductionOrderService service,
        CancellationToken cancellationToken) =>
        Results.Ok(await service.ListAsync(new ProductionOrderListQuery(search, status, assignedToMe ?? false), cancellationToken));

    private static async Task<IResult> GetAsync(Guid id, IProductionOrderService service, CancellationToken cancellationToken) =>
        Map(await service.GetAsync(id, cancellationToken));

    private static async Task<IResult> CreateAsync(SaveCommercialOrderCommand request, IProductionOrderService service, CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(request, cancellationToken);
        return result.Status == ProductionOrderOperationStatus.Success
            ? Results.Created($"/api/commercial/production-orders/{result.Order!.Id}", result.Order)
            : Map(result);
    }

    private static async Task<IResult> UpdateCommercialAsync(Guid id, SaveCommercialOrderCommand request, IProductionOrderService service, CancellationToken cancellationToken) =>
        Map(await service.UpdateCommercialAsync(id, request, cancellationToken));

    private static async Task<IResult> PreviewQuotationAsync(HttpRequest request, IProductionOrderService service, CancellationToken cancellationToken)
    {
        var form = await request.ReadFormAsync(cancellationToken);
        var file = form.Files.GetFile("file");
        if (file is null) return MissingFile();
        await using var stream = file.OpenReadStream();
        var result = await service.PreviewQuotationAsync(ToCommand(file, stream), cancellationToken);
        return result.Success ? Results.Ok(result.Preview) : Results.BadRequest(new { code = result.ErrorCode, message = result.ErrorMessage });
    }

    private static async Task<IResult> ImportQuotationAsync(HttpRequest request, IProductionOrderService service, CancellationToken cancellationToken)
    {
        var form = await request.ReadFormAsync(cancellationToken);
        var file = form.Files.GetFile("file");
        if (file is null) return MissingFile();
        if (!TrySelection(form, out var selection)) return InvalidSelection();
        if (!TryCommercial(form, out var commercial))
            return Results.BadRequest(new { code = "commercial_payload_invalid", message = "No fue posible leer los datos comerciales revisados." });
        Guid? relatedOrderId = null;
        if (!string.IsNullOrWhiteSpace(form["relatedOrderId"]))
        {
            if (!Guid.TryParse(form["relatedOrderId"], out var parsedRelatedOrderId))
                return Results.BadRequest(new { code = "related_order_invalid", message = "La OP relacionada no es válida." });
            relatedOrderId = parsedRelatedOrderId;
        }
        await using var stream = file.OpenReadStream();
        var result = await service.ImportQuotationAsync(ToCommand(file, stream), selection, commercial, relatedOrderId, cancellationToken);
        return result.Status == ProductionOrderOperationStatus.Success
            ? Results.Created($"/api/commercial/production-orders/{result.Order!.Id}", result.Order)
            : Map(result);
    }

    private static async Task<IResult> ReplaceQuotationAsync(Guid id, HttpRequest request, IProductionOrderService service, CancellationToken cancellationToken)
    {
        var form = await request.ReadFormAsync(cancellationToken);
        var file = form.Files.GetFile("file");
        if (file is null) return MissingFile();
        if (!TrySelection(form, out var selection) || !int.TryParse(form["version"], out var version)) return InvalidSelection();
        await using var stream = file.OpenReadStream();
        return Map(await service.ReplaceQuotationAsync(id, version, ToCommand(file, stream), selection, cancellationToken));
    }

    private static async Task<IResult> AddDocumentAsync(Guid id, string type, HttpRequest request, IProductionOrderService service, CancellationToken cancellationToken)
    {
        var form = await request.ReadFormAsync(cancellationToken);
        var file = form.Files.GetFile("file");
        if (file is null) return MissingFile();
        if (!int.TryParse(form["version"], out var version)) return InvalidSelection();
        await using var stream = file.OpenReadStream();
        return Map(await service.AddDocumentAsync(id, version, type, ToCommand(file, stream), cancellationToken));
    }

    private static async Task<IResult> SetDocumentApplicabilityAsync(Guid id, string type, ApplicabilityRequest request, IProductionOrderService service, CancellationToken cancellationToken) =>
        Map(await service.SetDocumentApplicabilityAsync(id, request.Version, type, request.NotApplicable, cancellationToken));

    private static async Task<IResult> DownloadDocumentAsync(Guid id, Guid documentId, IProductionOrderService service, CancellationToken cancellationToken)
    {
        var result = await service.DownloadDocumentAsync(id, documentId, cancellationToken);
        return result.Success
            ? Results.File(result.Content!, result.ContentType, result.FileName, enableRangeProcessing: true)
            : Results.NotFound(new { code = result.ErrorCode, message = result.ErrorMessage });
    }

    private static async Task<IResult> DeleteDocumentAsync(Guid id, Guid documentId, int version, IProductionOrderService service, CancellationToken cancellationToken) =>
        Map(await service.DeleteDocumentAsync(id, documentId, version, cancellationToken));

    private static async Task<IResult> DiscardDraftAsync(Guid id, int version, IProductionOrderService service, CancellationToken cancellationToken)
    {
        var result = await service.DiscardDraftAsync(id, version, cancellationToken);
        return result.Status == ProductionOrderOperationStatus.Success
            ? Results.Ok(new { discarded = true })
            : Map(result);
    }

    private static async Task<IResult> GetReviewersAsync(Guid id, IProductionOrderService service, CancellationToken cancellationToken)
    {
        var result = await service.GetReviewersAsync(id, cancellationToken);
        return result.Status == ProductionOrderOperationStatus.Success ? Results.Ok(result.Reviewers)
            : Map(new ProductionOrderOperationResult(result.Status, ErrorMessage: result.ErrorMessage));
    }

    private static async Task<IResult> SubmitForReviewAsync(Guid id, ReviewSubmissionRequest request, IProductionOrderService service, CancellationToken cancellationToken) =>
        Map(await service.SubmitForReviewAsync(id, request.Version, request.ReviewerUserId, cancellationToken));

    private static async Task<IResult> ReturnForCorrectionAsync(Guid id, ReturnRequest request, IProductionOrderService service, CancellationToken cancellationToken) =>
        Map(await service.ReturnForCorrectionAsync(id, request.Version, request.Reason, cancellationToken));

    private static async Task<IResult> SubmitAsync(Guid id, ApprovalRequest request, IProductionOrderService service, CancellationToken cancellationToken) =>
        Map(await service.SubmitAsync(id, request.Version, request.CustomerOrderNumber, cancellationToken));

    private static async Task<IResult> DuplicateAsync(Guid id, IProductionOrderService service, CancellationToken cancellationToken) =>
        Map(await service.DuplicateAsync(id, cancellationToken));

    private static async Task<IResult> ReceiveAsync(Guid id, VersionRequest request, IProductionOrderService service, CancellationToken cancellationToken) =>
        Map(await service.ReceiveAsync(id, request.Version, cancellationToken));

    private static async Task<IResult> UpdateProductionAsync(Guid id, SaveProductionOrderCommand request, IProductionOrderService service, CancellationToken cancellationToken) =>
        Map(await service.UpdateProductionAsync(id, request, cancellationToken));

    private static async Task<IResult> CompleteAsync(Guid id, VersionRequest request, IProductionOrderService service, CancellationToken cancellationToken) =>
        Map(await service.CompleteAsync(id, request.Version, cancellationToken));

    private static async Task<IResult> CancelAsync(Guid id, CancelRequest request, IProductionOrderService service, CancellationToken cancellationToken) =>
        Map(await service.CancelAsync(id, request.Version, request.Reason, cancellationToken));

    private static IResult Map(ProductionOrderOperationResult result)
    {
        if (result.Status == ProductionOrderOperationStatus.Success)
        {
            return Results.Ok(result.Order);
        }

        var statusCode = result.Status switch
        {
            ProductionOrderOperationStatus.NotFound => StatusCodes.Status404NotFound,
            ProductionOrderOperationStatus.Forbidden => StatusCodes.Status403Forbidden,
            ProductionOrderOperationStatus.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest,
        };
        return Results.Json(new
        {
            code = result.ErrorCode ?? "production_order_error",
            message = result.ErrorMessage ?? "No fue posible completar la operación.",
            errors = result.ValidationErrors,
        }, statusCode: statusCode);
    }

    private static bool TryCommercial(IFormCollection form, out SaveCommercialOrderCommand? commercial)
    {
        commercial = null;
        var value = form["commercial"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(value)) return true;
        try
        {
            commercial = JsonSerializer.Deserialize<SaveCommercialOrderCommand>(value, FormJsonOptions);
            return commercial is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public sealed record VersionRequest(int Version);

    public sealed record ReviewSubmissionRequest(int Version, Guid? ReviewerUserId);

    public sealed record ApprovalRequest(int Version, string? CustomerOrderNumber);

    public sealed record CancelRequest(int Version, string? Reason);

    public sealed record ReturnRequest(int Version, string? Reason);

    public sealed record ApplicabilityRequest(int Version, bool NotApplicable);

    private static UploadedFileCommand ToCommand(IFormFile file, Stream stream) =>
        new(file.FileName, file.ContentType, file.Length, stream);

    private static bool TrySelection(IFormCollection form, out ImportQuotationSelection selection)
    {
        selection = default!;
        if (!int.TryParse(form["itemIndex"], out var itemIndex) || !int.TryParse(form["optionIndex"], out var optionIndex)) return false;
        selection = new ImportQuotationSelection(itemIndex, optionIndex);
        return true;
    }

    private static IResult MissingFile() => Results.BadRequest(new { code = "file_required", message = "Selecciona un archivo." });

    private static IResult InvalidSelection() => Results.BadRequest(new { code = "invalid_form", message = "La selección o la versión no es válida." });
}
