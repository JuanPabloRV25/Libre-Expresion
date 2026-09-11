using Portal.Application.Identity;

namespace Portal.Api.Authentication;

public sealed class RequiredPasswordChangeMiddleware(RequestDelegate next)
{
    private static readonly HashSet<PathString> AllowedPaths =
    [
        new("/api/auth/csrf"),
        new("/api/auth/me"),
        new("/api/auth/logout"),
        new("/api/auth/change-required-password"),
        new("/api/auth/reset-password"),
        new("/health"),
        new("/api/health"),
    ];

    public async Task InvokeAsync(
        HttpContext context,
        ICurrentUserService currentUserService)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var currentUser = await currentUserService.GetCurrentAsync(
                context.RequestAborted);

            if (currentUser is null || !currentUser.IsActive)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            if (currentUser.MustChangePassword
                && !AllowedPaths.Contains(context.Request.Path))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(
                    new ApiErrorResponse(
                        "password_change_required",
                        "Debe establecer una contraseña definitiva antes de continuar."),
                    context.RequestAborted);
                return;
            }
        }

        await next(context);
    }
}
