using Portal.Api.Authentication;
using Portal.Application.Areas;
using Portal.Domain.Permissions;

namespace Portal.Api.Areas;

public static class AreaEndpoints
{
    public static IEndpointRouteBuilder MapAreaEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/areas");

        group.MapGet("/", ListAsync)
            .RequireAuthorization(PermissionCodes.AreasView);

        group.MapGet("/{id:guid}", GetAsync)
            .RequireAuthorization(PermissionCodes.AreasView);

        group.MapPost("/", CreateAsync)
            .RequireAuthorization(PermissionCodes.AreasCreate)
            .AddEndpointFilter<AntiforgeryEndpointFilter>();

        group.MapPut("/{id:guid}", UpdateAsync)
            .RequireAuthorization(PermissionCodes.AreasEdit)
            .AddEndpointFilter<AntiforgeryEndpointFilter>();

        group.MapPatch("/{id:guid}/status", SetStatusAsync)
            .RequireAuthorization(PermissionCodes.AreasActivate)
            .AddEndpointFilter<AntiforgeryEndpointFilter>();

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        string? search,
        string? status,
        IAreaService areaService,
        CancellationToken cancellationToken)
    {
        if (!TryParseStatus(status, out var isActive))
        {
            return Error(
                StatusCodes.Status400BadRequest,
                "invalid_status_filter",
                "El estado debe ser 'active' o 'inactive'.");
        }

        var areas = await areaService.ListAsync(
            search,
            isActive,
            cancellationToken);
        return Results.Ok(areas);
    }

    private static async Task<IResult> GetAsync(
        Guid id,
        IAreaService areaService,
        CancellationToken cancellationToken)
    {
        var area = await areaService.GetAsync(id, cancellationToken);
        return area is null
            ? Error(
                StatusCodes.Status404NotFound,
                "area_not_found",
                "El área solicitada no existe.")
            : Results.Ok(area);
    }

    private static async Task<IResult> CreateAsync(
        AreaRequest request,
        IAreaService areaService,
        CancellationToken cancellationToken)
    {
        var result = await areaService.CreateAsync(
            new CreateAreaCommand(request.Name, request.Description),
            cancellationToken);

        if (result.Status == AreaOperationStatus.Success)
        {
            return Results.Created(
                $"/api/areas/{result.Area!.Id}",
                result.Area);
        }

        return MapError(result);
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        AreaRequest request,
        IAreaService areaService,
        CancellationToken cancellationToken)
    {
        var result = await areaService.UpdateAsync(
            id,
            new UpdateAreaCommand(request.Name, request.Description),
            cancellationToken);
        return result.Status == AreaOperationStatus.Success
            ? Results.Ok(result.Area)
            : MapError(result);
    }

    private static async Task<IResult> SetStatusAsync(
        Guid id,
        AreaStatusRequest request,
        IAreaService areaService,
        CancellationToken cancellationToken)
    {
        var result = await areaService.SetStatusAsync(
            id,
            request.IsActive,
            cancellationToken);
        return result.Status == AreaOperationStatus.Success
            ? Results.Ok(result.Area)
            : MapError(result);
    }

    private static IResult MapError(AreaOperationResult result) =>
        result.Status switch
        {
            AreaOperationStatus.NotFound => Error(
                StatusCodes.Status404NotFound,
                result.ErrorCode!,
                result.ErrorMessage!),
            AreaOperationStatus.Duplicate => Error(
                StatusCodes.Status409Conflict,
                result.ErrorCode!,
                result.ErrorMessage!),
            _ => Error(
                StatusCodes.Status400BadRequest,
                result.ErrorCode ?? "validation_error",
                result.ErrorMessage ?? "Los datos del área no son válidos."),
        };

    private static bool TryParseStatus(string? status, out bool? isActive)
    {
        isActive = null;
        if (string.IsNullOrWhiteSpace(status))
        {
            return true;
        }

        if (status.Equals("active", StringComparison.OrdinalIgnoreCase))
        {
            isActive = true;
            return true;
        }

        if (status.Equals("inactive", StringComparison.OrdinalIgnoreCase))
        {
            isActive = false;
            return true;
        }

        return false;
    }

    private static IResult Error(int statusCode, string code, string message) =>
        Results.Json(
            new ApiErrorResponse(code, message),
            statusCode: statusCode);

    public sealed record AreaRequest(string? Name, string? Description);

    public sealed record AreaStatusRequest(bool IsActive);
}
