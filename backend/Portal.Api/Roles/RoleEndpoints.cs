using Portal.Api.Authentication;
using Portal.Application.Roles;
using Portal.Domain.Permissions;

namespace Portal.Api.Roles;

public static class RoleEndpoints
{
    public static IEndpointRouteBuilder MapRoleEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/roles");

        group.MapGet("/", ListAsync)
            .RequireAuthorization(PermissionCodes.RolesView);

        group.MapGet("/{id:guid}", GetAsync)
            .RequireAuthorization(PermissionCodes.RolesView);

        group.MapPost("/", CreateAsync)
            .RequireAuthorization(PermissionCodes.RolesCreate)
            .AddEndpointFilter<AntiforgeryEndpointFilter>();

        group.MapPut("/{id:guid}", UpdateAsync)
            .RequireAuthorization(PermissionCodes.RolesEdit)
            .AddEndpointFilter<AntiforgeryEndpointFilter>();

        group.MapPatch("/{id:guid}/status", SetStatusAsync)
            .RequireAuthorization(PermissionCodes.RolesActivate)
            .AddEndpointFilter<AntiforgeryEndpointFilter>();

        group.MapPut("/{id:guid}/permissions", UpdatePermissionsAsync)
            .RequireAuthorization(PermissionCodes.RolesAssignPermissions)
            .AddEndpointFilter<AntiforgeryEndpointFilter>();

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        string? search,
        string? status,
        IRoleService roleService,
        CancellationToken cancellationToken)
    {
        if (!TryParseStatus(status, out var isActive))
        {
            return Error(
                StatusCodes.Status400BadRequest,
                "invalid_status_filter",
                "El estado debe ser 'active' o 'inactive'.");
        }

        return Results.Ok(await roleService.ListAsync(
            search,
            isActive,
            cancellationToken));
    }

    private static async Task<IResult> GetAsync(
        Guid id,
        IRoleService roleService,
        CancellationToken cancellationToken)
    {
        var role = await roleService.GetAsync(id, cancellationToken);
        return role is null
            ? Error(
                StatusCodes.Status404NotFound,
                "role_not_found",
                "El rol solicitado no existe.")
            : Results.Ok(role);
    }

    private static async Task<IResult> CreateAsync(
        RoleRequest request,
        IRoleService roleService,
        CancellationToken cancellationToken)
    {
        var result = await roleService.CreateAsync(
            new CreateRoleCommand(request.Name, request.Description),
            cancellationToken);
        return result.Status == RoleOperationStatus.Success
            ? Results.Created($"/api/roles/{result.Role!.Id}", result.Role)
            : MapError(result);
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        RoleRequest request,
        IRoleService roleService,
        CancellationToken cancellationToken)
    {
        var result = await roleService.UpdateAsync(
            id,
            new UpdateRoleCommand(request.Name, request.Description),
            cancellationToken);
        return result.Status == RoleOperationStatus.Success
            ? Results.Ok(result.Role)
            : MapError(result);
    }

    private static async Task<IResult> SetStatusAsync(
        Guid id,
        RoleStatusRequest request,
        IRoleService roleService,
        CancellationToken cancellationToken)
    {
        var result = await roleService.SetStatusAsync(
            id,
            request.IsActive,
            cancellationToken);
        return result.Status == RoleOperationStatus.Success
            ? Results.Ok(result.Role)
            : MapError(result);
    }

    private static async Task<IResult> UpdatePermissionsAsync(
        Guid id,
        RolePermissionsRequest request,
        IRoleService roleService,
        CancellationToken cancellationToken)
    {
        if (request.PermissionCodes is null)
        {
            return Error(
                StatusCodes.Status400BadRequest,
                "invalid_permission_codes",
                "La lista de permisos es obligatoria.");
        }

        var result = await roleService.UpdatePermissionsAsync(
            id,
            request.PermissionCodes,
            cancellationToken);
        return result.Status == RoleOperationStatus.Success
            ? Results.Ok(result.Role)
            : MapError(result);
    }

    private static IResult MapError(RoleOperationResult result) =>
        result.Status switch
        {
            RoleOperationStatus.NotFound => Error(
                StatusCodes.Status404NotFound,
                result.ErrorCode!,
                result.ErrorMessage!),
            RoleOperationStatus.Duplicate
                or RoleOperationStatus.Protected
                or RoleOperationStatus.Conflict => Error(
                    StatusCodes.Status409Conflict,
                    result.ErrorCode!,
                    result.ErrorMessage!),
            _ => Error(
                StatusCodes.Status400BadRequest,
                result.ErrorCode ?? "validation_error",
                result.ErrorMessage ?? "Los datos del rol no son válidos."),
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

    public sealed record RoleRequest(string? Name, string? Description);

    public sealed record RoleStatusRequest(bool IsActive);

    public sealed record RolePermissionsRequest(
        IReadOnlyList<string>? PermissionCodes);
}
