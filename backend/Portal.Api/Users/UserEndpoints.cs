using Portal.Api.Authentication;
using Portal.Application.Users;
using Portal.Domain.Permissions;

namespace Portal.Api.Users;

public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/users");

        group.MapGet("/", ListAsync)
            .RequireAuthorization(PermissionCodes.UsersView);

        group.MapGet("/{id:guid}", GetAsync)
            .RequireAuthorization(PermissionCodes.UsersView);

        group.MapPost("/", CreateAsync)
            .RequireAuthorization(PermissionCodes.UsersCreate)
            .AddEndpointFilter<AntiforgeryEndpointFilter>();

        group.MapPut("/{id:guid}", UpdateAsync)
            .RequireAuthorization(PermissionCodes.UsersEdit)
            .AddEndpointFilter<AntiforgeryEndpointFilter>();

        group.MapPatch("/{id:guid}/status", SetStatusAsync)
            .RequireAuthorization(PermissionCodes.UsersActivate)
            .AddEndpointFilter<AntiforgeryEndpointFilter>();

        group.MapPut("/{id:guid}/roles", UpdateRolesAsync)
            .RequireAuthorization(PermissionCodes.UsersAssignRoles)
            .AddEndpointFilter<AntiforgeryEndpointFilter>();

        group.MapPost("/{id:guid}/reset-password", ResetPasswordAsync)
            .RequireAuthorization(PermissionCodes.UsersResetPassword)
            .AddEndpointFilter<AntiforgeryEndpointFilter>();

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        string? search,
        string? status,
        Guid? areaId,
        Guid? roleId,
        IUserService userService,
        CancellationToken cancellationToken)
    {
        if (!TryParseStatus(status, out var isActive))
        {
            return Error(
                StatusCodes.Status400BadRequest,
                "invalid_status_filter",
                "El estado debe ser 'active' o 'inactive'.");
        }

        return Results.Ok(await userService.ListAsync(
            search,
            isActive,
            areaId,
            roleId,
            cancellationToken));
    }

    private static async Task<IResult> GetAsync(
        Guid id,
        IUserService userService,
        CancellationToken cancellationToken)
    {
        var user = await userService.GetAsync(id, cancellationToken);
        return user is null
            ? Error(
                StatusCodes.Status404NotFound,
                "user_not_found",
                "El usuario solicitado no existe.")
            : Results.Ok(user);
    }

    private static async Task<IResult> CreateAsync(
        CreateUserRequest request,
        IUserService userService,
        CancellationToken cancellationToken)
    {
        var result = await userService.CreateAsync(
            new CreateUserCommand(
                request.DocumentNumber,
                request.FirstName,
                request.LastName,
                request.Email,
                request.AreaId,
                request.AdvisorCode,
                request.RoleIds,
                request.IsActive ?? true),
            cancellationToken);
        return result.Status == UserOperationStatus.Success
            ? Results.Created(
                $"/api/users/{result.User!.Id}",
                new UserCreatedResponse(
                    result.User,
                    "pending_integration"))
            : MapError(result);
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateUserRequest request,
        IUserService userService,
        CancellationToken cancellationToken)
    {
        var result = await userService.UpdateAsync(
            id,
            new UpdateUserCommand(
                request.FirstName,
                request.LastName,
                request.Email,
                request.AreaId,
                request.AdvisorCode),
            cancellationToken);
        return result.Status == UserOperationStatus.Success
            ? Results.Ok(result.User)
            : MapError(result);
    }

    private static async Task<IResult> SetStatusAsync(
        Guid id,
        UserStatusRequest request,
        IUserService userService,
        CancellationToken cancellationToken)
    {
        var result = await userService.SetStatusAsync(
            id,
            request.IsActive,
            cancellationToken);
        return result.Status == UserOperationStatus.Success
            ? Results.Ok(result.User)
            : MapError(result);
    }

    private static async Task<IResult> UpdateRolesAsync(
        Guid id,
        UserRolesRequest request,
        IUserService userService,
        CancellationToken cancellationToken)
    {
        if (request.RoleIds is null)
        {
            return Error(
                StatusCodes.Status400BadRequest,
                "invalid_role_ids",
                "La lista de roles es obligatoria.");
        }

        var result = await userService.UpdateRolesAsync(
            id,
            request.RoleIds,
            cancellationToken);
        return result.Status == UserOperationStatus.Success
            ? Results.Ok(result.User)
            : MapError(result);
    }

    private static async Task<IResult> ResetPasswordAsync(
        Guid id,
        IUserService userService,
        CancellationToken cancellationToken)
    {
        var result = await userService.ResetPasswordAsync(id, cancellationToken);
        return result.Status == UserOperationStatus.Success
            ? Results.Ok(new PasswordResetResponse(
                PasswordReset: true,
                NotificationStatus: "pending_integration"))
            : MapError(result);
    }

    private static IResult MapError(UserOperationResult result) =>
        result.Status switch
        {
            UserOperationStatus.NotFound => Error(
                StatusCodes.Status404NotFound,
                result.ErrorCode!,
                result.ErrorMessage!),
            UserOperationStatus.DuplicateDocument
                or UserOperationStatus.DuplicateEmail
                or UserOperationStatus.LastSuperadmin
                or UserOperationStatus.Conflict => Error(
                    StatusCodes.Status409Conflict,
                    result.ErrorCode!,
                    result.ErrorMessage!),
            _ => Error(
                StatusCodes.Status400BadRequest,
                result.ErrorCode ?? "validation_error",
                result.ErrorMessage ?? "Los datos del usuario no son válidos."),
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

    public sealed record CreateUserRequest(
        string? DocumentNumber,
        string? FirstName,
        string? LastName,
        string? Email,
        Guid? AreaId,
        string? AdvisorCode,
        IReadOnlyList<Guid>? RoleIds,
        bool? IsActive);

    public sealed record UpdateUserRequest(
        string? FirstName,
        string? LastName,
        string? Email,
        Guid? AreaId,
        string? AdvisorCode);

    public sealed record UserStatusRequest(bool IsActive);

    public sealed record UserRolesRequest(IReadOnlyList<Guid>? RoleIds);

    public sealed record UserCreatedResponse(
        UserDto User,
        string NotificationStatus);

    public sealed record PasswordResetResponse(
        bool PasswordReset,
        string NotificationStatus);
}
