using Microsoft.AspNetCore.Mvc;
using Portal.Api.Authentication;
using Portal.Application.Auditing;
using Portal.Domain.Permissions;

namespace Portal.Api.Audit;

public static class AuditEndpoints
{
    private const int MaximumPageSize = 100;
    private const int MaximumPage = 1_000_000;

    public static IEndpointRouteBuilder MapAuditEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGroup("/api/audit")
            .MapGet("/", ListAsync)
            .RequireAuthorization(PermissionCodes.AuditView);

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        [AsParameters] AuditQueryParameters parameters,
        IAuditService auditService,
        CancellationToken cancellationToken)
    {
        var page = parameters.Page ?? 1;
        var pageSize = parameters.PageSize ?? 25;

        if (page < 1 || page > MaximumPage)
        {
            return Error(
                "invalid_page",
                $"La página debe estar entre 1 y {MaximumPage}.");
        }

        if (pageSize < 1)
        {
            return Error(
                "invalid_page_size",
                "El tamaño de página debe ser mayor que cero.");
        }

        if (parameters.DateFrom.HasValue
            && parameters.DateTo.HasValue
            && parameters.DateFrom.Value > parameters.DateTo.Value)
        {
            return Error(
                "invalid_date_range",
                "La fecha inicial no puede ser posterior a la fecha final.");
        }

        var query = new AuditQuery(
            page,
            Math.Min(pageSize, MaximumPageSize),
            parameters.Action,
            parameters.EntityType,
            parameters.Result,
            parameters.ActorUserId,
            parameters.DateFrom,
            parameters.DateTo,
            parameters.Search);

        return Results.Ok(await auditService.ListAsync(query, cancellationToken));
    }

    private static IResult Error(string code, string message) =>
        Results.Json(
            new ApiErrorResponse(code, message),
            statusCode: StatusCodes.Status400BadRequest);

    public sealed class AuditQueryParameters
    {
        public int? Page { get; init; }

        public int? PageSize { get; init; }

        public string? Action { get; init; }

        public string? EntityType { get; init; }

        public string? Result { get; init; }

        public Guid? ActorUserId { get; init; }

        public DateTimeOffset? DateFrom { get; init; }

        public DateTimeOffset? DateTo { get; init; }

        public string? Search { get; init; }
    }
}
