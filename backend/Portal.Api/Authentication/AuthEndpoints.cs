using Microsoft.AspNetCore.Antiforgery;
using Portal.Application.Identity;

namespace Portal.Api.Authentication;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/auth");

        group.MapGet("/csrf", GetCsrfAsync)
            .AllowAnonymous();

        group.MapPost("/login", LoginAsync)
            .AllowAnonymous()
            .AddEndpointFilter<AntiforgeryEndpointFilter>();

        group.MapGet("/me", GetCurrentUserAsync)
            .RequireAuthorization();

        group.MapPost("/logout", LogoutAsync)
            .RequireAuthorization()
            .AddEndpointFilter<AntiforgeryEndpointFilter>();

        group.MapPost(
                "/change-required-password",
                ChangeRequiredPasswordAsync)
            .RequireAuthorization()
            .AddEndpointFilter<AntiforgeryEndpointFilter>();

        group.MapPost("/reset-password", ResetPasswordAsync)
            .AllowAnonymous()
            .AddEndpointFilter<AntiforgeryEndpointFilter>();

        group.MapPost("/change-password", ChangePasswordAsync)
            .RequireAuthorization()
            .AddEndpointFilter<AntiforgeryEndpointFilter>();

        return endpoints;
    }

    private static IResult GetCsrfAsync(
        HttpContext context,
        IAntiforgery antiforgery)
    {
        var tokenSet = antiforgery.GetAndStoreTokens(context);
        context.Response.Headers.CacheControl = "no-store";

        return Results.Ok(new CsrfResponse(
            tokenSet.RequestToken
            ?? throw new InvalidOperationException(
                "ASP.NET Core did not generate an antiforgery request token.")));
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        IPortalAuthenticationService authenticationService)
    {
        var result = await authenticationService.LoginAsync(
            request.DocumentNumber,
            request.Password);

        return result.Status switch
        {
            PortalLoginStatus.Succeeded => Results.Ok(new LoginResponse(
                Authenticated: true,
                result.MustChangePassword)),
            PortalLoginStatus.Inactive => Error(
                StatusCodes.Status403Forbidden,
                "inactive_account",
                "La cuenta está inactiva."),
            PortalLoginStatus.LockedOut => Error(
                StatusCodes.Status423Locked,
                "account_locked",
                "La cuenta está temporalmente bloqueada."),
            _ => Error(
                StatusCodes.Status401Unauthorized,
                "invalid_credentials",
                "Número de documento o contraseña incorrectos."),
        };
    }

    private static async Task<IResult> GetCurrentUserAsync(
        ICurrentUserService currentUserService,
        CancellationToken cancellationToken)
    {
        var currentUser = await currentUserService.GetCurrentAsync(
            cancellationToken);

        return currentUser is null
            ? Results.Unauthorized()
            : Results.Ok(new CurrentUserResponse(
                currentUser.Id,
                currentUser.DocumentNumber,
                currentUser.FirstName,
                currentUser.LastName,
                currentUser.Email,
                currentUser.Area is null
                    ? null
                    : new CurrentUserAreaResponse(
                        currentUser.Area.Id,
                        currentUser.Area.Name),
                currentUser.Roles,
                currentUser.Permissions,
                currentUser.IsActive,
                currentUser.MustChangePassword));
    }

    private static async Task<IResult> LogoutAsync(
        IPortalAuthenticationService authenticationService)
    {
        await authenticationService.SignOutAsync();
        return Results.Ok(new OperationResponse(true));
    }

    private static async Task<IResult> ChangeRequiredPasswordAsync(
        RequiredPasswordChangeRequest request,
        ICurrentUserService currentUserService,
        IPortalAuthenticationService authenticationService,
        CancellationToken cancellationToken)
    {
        var currentUser = await currentUserService.GetCurrentAsync(
            cancellationToken);

        if (currentUser is null)
        {
            return Results.Unauthorized();
        }

        var result = await authenticationService.ChangeRequiredPasswordAsync(
            currentUser.Id,
            request.NewPassword,
            request.ConfirmPassword,
            cancellationToken);

        return MapPasswordChangeResult(result);
    }

    private static async Task<IResult> ChangePasswordAsync(
        PasswordChangeRequest request,
        ICurrentUserService currentUserService,
        IPortalAuthenticationService authenticationService,
        CancellationToken cancellationToken)
    {
        var currentUser = await currentUserService.GetCurrentAsync(
            cancellationToken);

        if (currentUser is null)
        {
            return Results.Unauthorized();
        }

        var result = await authenticationService.ChangePasswordAsync(
            currentUser.Id,
            request.CurrentPassword,
            request.NewPassword,
            request.ConfirmPassword,
            cancellationToken);

        return MapPasswordChangeResult(result);
    }

    private static async Task<IResult> ResetPasswordAsync(
        PasswordResetRequest request,
        IPortalAuthenticationService authenticationService,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(request.UserId, out var userId))
        {
            return InvalidPasswordResetLink();
        }

        var result = await authenticationService.CompletePasswordResetAsync(
            userId,
            request.Token ?? string.Empty,
            request.NewPassword ?? string.Empty,
            request.ConfirmPassword ?? string.Empty,
            cancellationToken);

        return result.Status switch
        {
            PasswordResetCompletionStatus.Succeeded => Results.Ok(
                new PasswordResetCompletedResponse(
                    PasswordReset: true,
                    result.NotificationStatus!)),
            PasswordResetCompletionStatus.ConfirmationMismatch => Error(
                StatusCodes.Status400BadRequest,
                "password_confirmation_mismatch",
                "La confirmación no coincide con la nueva contraseña."),
            PasswordResetCompletionStatus.InvalidPassword => Error(
                StatusCodes.Status400BadRequest,
                "invalid_new_password",
                "La nueva contraseña no cumple la política de seguridad."),
            _ => InvalidPasswordResetLink(),
        };
    }

    private static IResult InvalidPasswordResetLink() => Error(
        StatusCodes.Status400BadRequest,
        "invalid_or_expired_password_reset",
        "El enlace es inválido, venció o ya fue utilizado.");

    private static IResult MapPasswordChangeResult(
        PasswordChangeResult result) => result.Status switch
        {
            PasswordChangeStatus.Succeeded => Results.Ok(
                new PasswordChangeResponse(true, result.NotificationStatus!)),
            PasswordChangeStatus.ConfirmationMismatch => Error(
                StatusCodes.Status400BadRequest,
                "password_confirmation_mismatch",
                "La confirmación no coincide con la nueva contraseña."),
            PasswordChangeStatus.InvalidCurrentPassword => Error(
                StatusCodes.Status400BadRequest,
                "invalid_current_password",
                "La contraseña actual no es correcta."),
            PasswordChangeStatus.InvalidPassword => Error(
                StatusCodes.Status400BadRequest,
                "invalid_new_password",
                "La nueva contraseña no cumple la política de seguridad."),
            PasswordChangeStatus.ChangeNotRequired => Error(
                StatusCodes.Status409Conflict,
                "password_change_not_required",
                "La cuenta no requiere el cambio obligatorio de contraseña."),
            PasswordChangeStatus.ChangeRequired => Error(
                StatusCodes.Status403Forbidden,
                "password_change_required",
                "Debe completar primero el cambio obligatorio de contraseña."),
            PasswordChangeStatus.Inactive => Error(
                StatusCodes.Status403Forbidden,
                "inactive_account",
                "La cuenta está inactiva."),
            _ => Results.Unauthorized(),
        };

    private static IResult Error(
        int statusCode,
        string code,
        string message) => Results.Json(
            new ApiErrorResponse(code, message),
            statusCode: statusCode);

    public sealed record CsrfResponse(string Token);

    public sealed record LoginRequest(
        string DocumentNumber,
        string Password);

    public sealed record LoginResponse(
        bool Authenticated,
        bool MustChangePassword);

    public sealed record CurrentUserAreaResponse(Guid Id, string Name);

    public sealed record CurrentUserResponse(
        Guid Id,
        string DocumentNumber,
        string FirstName,
        string LastName,
        string Email,
        CurrentUserAreaResponse? Area,
        IReadOnlyList<string> Roles,
        IReadOnlyList<string> Permissions,
        bool IsActive,
        bool MustChangePassword);

    public sealed record RequiredPasswordChangeRequest(
        string NewPassword,
        string ConfirmPassword);

    public sealed record PasswordChangeRequest(
        string CurrentPassword,
        string NewPassword,
        string ConfirmPassword);

    public sealed record PasswordResetRequest(
        string? UserId,
        string? Token,
        string? NewPassword,
        string? ConfirmPassword);

    public sealed record PasswordResetCompletedResponse(
        bool PasswordReset,
        string NotificationStatus);

    public sealed record OperationResponse(bool Succeeded);

    public sealed record PasswordChangeResponse(
        bool Succeeded,
        string NotificationStatus);
}
